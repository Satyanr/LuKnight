namespace LuKnight.Assistant;

public sealed record ToolExecutionResult(
    bool Success,
    string Message);

public interface IAssistantTool
{
    string Name { get; }

    Task<ToolExecutionResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default);
}
