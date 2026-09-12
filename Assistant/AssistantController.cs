using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class AssistantController
{
    private readonly ChatCoordinator _chat;

    public ConversationManager Conversation { get; } = new();
    public PersonalityEngine Personality { get; }
    public MemoryService Memory { get; }
    public AssistantContextProvider Context { get; }
    public bool IsBusy => _chat.IsBusy;
    public string DisplayName => _chat.DisplayName;

    public AssistantController(
        ChatCoordinator chat,
        PersonalityEngine? personality = null,
        MemoryService? memory = null,
        AssistantContextProvider? context = null)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        Personality = personality ?? new PersonalityEngine();
        Memory = memory ?? new MemoryService();
        Context = context ?? new AssistantContextProvider();
    }

    public async Task<AssistantReply> SendAsync(
        AssistantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("Pesan tidak boleh kosong.", nameof(request));

        // Rejected submissions must not add transcript entries.
        cancellationToken.ThrowIfCancellationRequested();
        if (IsBusy) throw new InvalidOperationException("Tunggu permintaan chat selesai.");

        MemoryCommand? memoryCommand = MemoryCommandParser.Parse(request.Text);
        if (memoryCommand is not null)
        {
            Conversation.AddUser(request);
            try
            {
                string memoryReply;
                if (memoryCommand.Kind == MemoryCommandKind.Remember)
                {
                    Memory.Remember(memoryCommand.Text);
                    memoryReply = "Baik, aku akan mengingat itu.";
                }
                else
                {
                    bool forgotten = Memory.ForgetExact(memoryCommand.Text);
                    memoryReply = forgotten
                        ? "Memory itu sudah kuhapus."
                        : "Aku tidak menemukan memory yang sama persis.";
                }

                Conversation.AddAssistant(memoryReply);
                return new AssistantReply(memoryReply, AssistantBackend.Local, DateTimeOffset.UtcNow);
            }
            catch
            {
                Conversation.RollbackPendingUser();
                throw;
            }
        }

        IReadOnlyList<ChatContextTurn> context = BuildShortTermContext();
        Conversation.AddUser(request);

        try
        {
            string personalityInstruction = Personality.BuildSystemInstruction();
            string runtimeInstruction = Context.BuildSystemInstruction();
            if (!string.IsNullOrWhiteSpace(runtimeInstruction))
                personalityInstruction += "\n\n" + runtimeInstruction;

            IReadOnlyList<string> longTermMemory = BuildLongTermMemoryContext(request.Text);

            string reply = await _chat.SendMessageAsync(
                request.Text,
                personalityInstruction,
                context,
                longTermMemory,
                cancellationToken);
            Conversation.AddAssistant(reply);

            AssistantBackend backend = _chat.LastReplyWasGemini ? AssistantBackend.Gemini : AssistantBackend.Local;
            return new AssistantReply(reply, backend, DateTimeOffset.UtcNow);
        }
        catch
        {
            Conversation.RollbackPendingUser();
            throw;
        }
    }

    private IReadOnlyList<string> BuildLongTermMemoryContext(string query)
    {
        return Memory.Search(query).Select(memory => memory.Text).ToArray();
    }

    private IReadOnlyList<ChatContextTurn> BuildShortTermContext()
    {
        if (!_chat.Options.RememberConversation)
            return Array.Empty<ChatContextTurn>();

        return Conversation.GetRecentContext()
            .Select(turn => new ChatContextTurn(
                turn.Role == ConversationRole.User
                    ? ChatContextRole.User
                    : ChatContextRole.Assistant,
                turn.Text))
            .ToArray();
    }

    public void ClearConversation()
    {
        _chat.ClearConversation();
        Conversation.Clear();
    }
}
