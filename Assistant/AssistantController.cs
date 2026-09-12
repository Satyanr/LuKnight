using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class AssistantController
{
    private readonly ChatCoordinator _chat;

    public ConversationManager Conversation { get; } = new();
    public PersonalityEngine Personality { get; }
    public MemoryService Memory { get; }
    public bool IsBusy => _chat.IsBusy;
    public string DisplayName => _chat.DisplayName;

    public AssistantController(
        ChatCoordinator chat,
        PersonalityEngine? personality = null,
        MemoryService? memory = null)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        Personality = personality ?? new PersonalityEngine();
        Memory = memory ?? new MemoryService();
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
            string memoryInstruction = BuildLongTermMemoryInstruction(request.Text);
            if (!string.IsNullOrWhiteSpace(memoryInstruction))
                personalityInstruction += "\n\n" + memoryInstruction;

            string reply = await _chat.SendMessageAsync(
                request.Text,
                personalityInstruction,
                context,
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

    private string BuildLongTermMemoryInstruction(string query)
    {
        IReadOnlyList<MemoryEntry> memories = Memory.Search(query);
        if (memories.Count == 0)
            return string.Empty;

        return """
            Long-term memory yang relevan.
            Gunakan hanya bila membantu menjawab pertanyaan.
            Jangan mengarang memory tambahan.

            """ + string.Join(
                Environment.NewLine,
                memories.Select(memory => "- Pengguna sebelumnya meminta agar diingat: " + memory.Text));
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
