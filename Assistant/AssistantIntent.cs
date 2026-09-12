namespace LuKnight.Assistant;

public enum AssistantIntentKind
{
    Conversation,
    Tool,
    Context
}

public sealed record ToolInvocation(
    string Name,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record ContextInvocation(
    string Name,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record AssistantIntent(
    AssistantIntentKind Kind,
    ToolInvocation? Tool = null,
    ContextInvocation? Context = null)
{
    public static AssistantIntent Conversation() =>
        new(AssistantIntentKind.Conversation);

    public static AssistantIntent UseTool(ToolInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return new AssistantIntent(AssistantIntentKind.Tool, invocation);
    }

    public static AssistantIntent UseContext(ContextInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return new AssistantIntent(AssistantIntentKind.Context, Context: invocation);
    }
}

public static class BuiltInToolNames
{
    public const string MemoryRemember = "memory.remember";
    public const string MemoryForget = "memory.forget";
    public const string DesktopListApplications = "desktop.list_applications";
}

public static class BuiltInContextNames
{
    public const string LocalTextFile = "context.file.text";
    public const string ClipboardText = "context.clipboard.text";
}
