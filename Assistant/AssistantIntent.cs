namespace LuKnight.Assistant;

public enum AssistantIntentKind
{
    Conversation,
    Tool,
    Context,
    Action,
    LocalResponse
}

public sealed record ToolInvocation(
    string Name,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record ContextInvocation(
    string Name,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record ActionInvocation(
    string Name,
    IReadOnlyDictionary<string, string> Arguments);

public sealed record AssistantIntent(
    AssistantIntentKind Kind,
    ToolInvocation? Tool = null,
    ContextInvocation? Context = null,
    ActionInvocation? Action = null,
    string? LocalText = null)
{
    public static AssistantIntent Conversation() =>
        new(AssistantIntentKind.Conversation);

    public static AssistantIntent UseTool(ToolInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return new AssistantIntent(AssistantIntentKind.Tool, Tool: invocation);
    }

    public static AssistantIntent UseContext(ContextInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return new AssistantIntent(AssistantIntentKind.Context, Context: invocation);
    }

    public static AssistantIntent UseAction(ActionInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return new AssistantIntent(AssistantIntentKind.Action, Action: invocation);
    }

    public static AssistantIntent RespondLocal(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Local response tidak boleh kosong.", nameof(text));
        return new(AssistantIntentKind.LocalResponse, LocalText: text.Trim());
    }
}

public static class BuiltInToolNames
{
    public const string MemoryRemember = "memory.remember";
    public const string MemoryForget = "memory.forget";
    public const string DesktopListApplications = "desktop.list_applications";
}

public static class BuiltInActionNames
{
    public const string DesktopOpenApplication = "desktop.open_application";
    public const string DesktopFocusApplication = "desktop.focus_application";
    public const string DesktopOpenFolder = "desktop.open_folder";
    public const string DesktopSearchExplorer = "desktop.search_explorer";
}

public static class BuiltInContextNames
{
    public const string LocalTextFile = "context.file.text";
    public const string ClipboardText = "context.clipboard.text";
    public const string SystemStatus = "context.system.status";
    public const string ScreenImage = "context.screen.image";
}
