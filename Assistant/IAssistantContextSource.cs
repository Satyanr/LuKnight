using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed record ContextCaptureResult(
    bool Success,
    string Message,
    ChatReferenceBlock? Reference = null);

public interface IAssistantContextSource
{
    string Name { get; }

    Task<ContextCaptureResult> CaptureAsync(
        ContextInvocation invocation,
        CancellationToken cancellationToken = default);
}
