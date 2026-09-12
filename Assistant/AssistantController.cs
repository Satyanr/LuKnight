using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class AssistantController
{
    private readonly ChatCoordinator _chat;

    public ConversationManager Conversation { get; } = new();
    public PersonalityEngine Personality { get; }
    public bool IsBusy => _chat.IsBusy;
    public string DisplayName => _chat.DisplayName;

    public AssistantController(ChatCoordinator chat, PersonalityEngine? personality = null)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        Personality = personality ?? new PersonalityEngine();
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

        IReadOnlyList<ChatContextTurn> context = BuildShortTermContext();
        Conversation.AddUser(request);

        try
        {
            string personalityInstruction = Personality.BuildSystemInstruction();
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
