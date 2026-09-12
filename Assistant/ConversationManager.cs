namespace LuKnight.Assistant;

/// <summary>Assistant transcript for this session; independent of provider context.</summary>
public sealed class ConversationManager
{
    private const int MaxSessionTurns = 100;
    private readonly List<ConversationTurn> _turns = new();

    public IReadOnlyList<ConversationTurn> Turns => _turns.AsReadOnly();
    public int Count => _turns.Count;

    public void AddUser(AssistantRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string text = Normalize(request.Text);
        _turns.Add(new ConversationTurn(ConversationRole.User, text, DateTimeOffset.UtcNow, request.Source));
        Trim();
    }

    public void AddAssistant(string text)
    {
        text = Normalize(text);
        _turns.Add(new ConversationTurn(ConversationRole.Assistant, text, DateTimeOffset.UtcNow));
        Trim();
    }

    public void Clear() => _turns.Clear();
    public IReadOnlyList<ConversationTurn> Snapshot() => _turns.ToArray();

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Conversation text tidak boleh kosong.", nameof(value));
        return value.Trim();
    }

    private void Trim()
    {
        if (_turns.Count > MaxSessionTurns)
            _turns.RemoveRange(0, _turns.Count - MaxSessionTurns);
    }
}
