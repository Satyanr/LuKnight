namespace LuKnight.Assistant;

public sealed class AssistantToolRouter
{
    private readonly Dictionary<string, IAssistantTool> _tools;

    public AssistantToolRouter(IEnumerable<IAssistantTool>? tools = null)
    {
        _tools = new Dictionary<string, IAssistantTool>(StringComparer.OrdinalIgnoreCase);
        if (tools is null)
            return;

        foreach (IAssistantTool tool in tools)
            Register(tool);
    }

    public IReadOnlyCollection<string> RegisteredTools => _tools.Keys.ToArray();

    public void Register(IAssistantTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        if (string.IsNullOrWhiteSpace(tool.Name))
            throw new ArgumentException("Tool name tidak boleh kosong.", nameof(tool));
        if (!_tools.TryAdd(tool.Name, tool))
            throw new InvalidOperationException($"Tool '{tool.Name}' sudah terdaftar.");
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_tools.TryGetValue(invocation.Name, out IAssistantTool? tool))
            return new ToolExecutionResult(false, "Perintah itu belum tersedia.");

        return await tool.ExecuteAsync(invocation, cancellationToken);
    }
}
