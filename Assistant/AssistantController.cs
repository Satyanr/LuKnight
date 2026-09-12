using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class AssistantController
{
    private readonly ChatCoordinator _chat;

    public ConversationManager Conversation { get; } = new();
    public bool IsBusy => _chat.IsBusy;
    public string DisplayName => _chat.DisplayName;

    public AssistantController(ChatCoordinator chat)
    {
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
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

        Conversation.AddUser(request);
        string reply = await _chat.SendMessageAsync(request.Text, cancellationToken);
        Conversation.AddAssistant(reply);

        AssistantBackend backend = _chat.LastReplyWasGemini ? AssistantBackend.Gemini : AssistantBackend.Local;
        return new AssistantReply(reply, backend, DateTimeOffset.UtcNow);
    }

    public void ClearConversation()
    {
        _chat.ClearConversation();
        Conversation.Clear();
    }
}
