using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class RememberMemoryTool : IAssistantTool
{
    private readonly MemoryService _memory;

    public string Name => BuiltInToolNames.MemoryRemember;

    public RememberMemoryTool(MemoryService memory) =>
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public Task<ToolExecutionResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!invocation.Arguments.TryGetValue("text", out string? text) || string.IsNullOrWhiteSpace(text))
            return Task.FromResult(new ToolExecutionResult(false, "Tidak ada informasi yang bisa diingat."));

        _memory.Remember(text);
        return Task.FromResult(new ToolExecutionResult(true, "Baik, aku akan mengingat itu."));
    }
}

public sealed class ForgetMemoryTool : IAssistantTool
{
    private readonly MemoryService _memory;

    public string Name => BuiltInToolNames.MemoryForget;

    public ForgetMemoryTool(MemoryService memory) =>
        _memory = memory ?? throw new ArgumentNullException(nameof(memory));

    public Task<ToolExecutionResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!invocation.Arguments.TryGetValue("text", out string? text) || string.IsNullOrWhiteSpace(text))
            return Task.FromResult(new ToolExecutionResult(false, "Tidak ada memory yang bisa dihapus."));

        bool removed = _memory.ForgetExact(text);
        return Task.FromResult(removed
            ? new ToolExecutionResult(true, "Memory itu sudah kuhapus.")
            : new ToolExecutionResult(false, "Aku tidak menemukan memory yang sama persis."));
    }
}
