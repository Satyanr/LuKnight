namespace LuKnight.Assistant;

public sealed class AssistantContextSourceRouter
{
    private readonly Dictionary<string, IAssistantContextSource> _sources;

    public AssistantContextSourceRouter(IEnumerable<IAssistantContextSource>? sources = null)
    {
        _sources = new Dictionary<string, IAssistantContextSource>(StringComparer.OrdinalIgnoreCase);
        if (sources is null)
            return;

        foreach (IAssistantContextSource source in sources)
            Register(source);
    }

    public IReadOnlyCollection<string> RegisteredSources => _sources.Keys.ToArray();

    public void Register(IAssistantContextSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.Name))
            throw new ArgumentException("Context source name tidak boleh kosong.", nameof(source));
        if (!_sources.TryAdd(source.Name, source))
            throw new InvalidOperationException($"Context source '{source.Name}' sudah terdaftar.");
    }

    public Task<ContextCaptureResult> CaptureAsync(
        ContextInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sources.TryGetValue(invocation.Name, out IAssistantContextSource? source))
            return Task.FromResult(new ContextCaptureResult(false, "Context source itu belum tersedia."));

        return source.CaptureAsync(invocation, cancellationToken);
    }
}
