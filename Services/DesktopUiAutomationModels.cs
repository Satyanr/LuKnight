using System.Windows;

namespace LuKnight.Services;

public sealed record DesktopUiReadOptions(
    int MaxDepth = 6,
    int MaxNodes = 250,
    int MaxTextLength = 160,
    TimeSpan? Timeout = null)
{
    public TimeSpan EffectiveTimeout => Timeout ?? TimeSpan.FromSeconds(4);
}

public sealed record DesktopUiNodeSnapshot(
    string Path,
    int Depth,
    string ControlType,
    string Name,
    string AutomationId,
    string ClassName,
    Rect Bounds,
    bool IsEnabled,
    bool IsOffscreen,
    bool HasKeyboardFocus,
    bool IsPassword)
{
    public bool IsProtected => IsPassword;

    public string DisplayName => IsPassword
        ? "[protected]"
        : string.IsNullOrWhiteSpace(Name)
            ? ControlType
            : Name;
}

public sealed record DesktopUiSnapshot(
    DesktopWindowTarget Window,
    IReadOnlyList<DesktopUiNodeSnapshot> Nodes,
    bool Truncated,
    string? Error = null)
{
    public bool Success => Error is null;

    public static DesktopUiSnapshot Failure(
        DesktopWindowTarget window,
        string error) =>
        new(window, Array.Empty<DesktopUiNodeSnapshot>(), false, error);
}

public static class DesktopUiControlTypes
{
    private static readonly HashSet<string> Interactive = new(
        StringComparer.OrdinalIgnoreCase)
    {
        "Button",
        "Edit",
        "List",
        "ListItem",
        "Menu",
        "MenuItem",
        "Tab",
        "TabItem",
        "ComboBox",
        "CheckBox",
        "RadioButton",
        "Tree",
        "TreeItem",
        "DataGrid",
        "DataItem",
        "Hyperlink",
        "Slider",
        "Spinner"
    };

    public static bool IsInteractive(string controlType) =>
        Interactive.Contains(controlType);
}

public static class DesktopUiSnapshotFormatter
{
    public static string Format(
        DesktopUiSnapshot snapshot,
        bool interactiveOnly = false)
    {
        if (!snapshot.Success)
            return $"UI Automation gagal: {snapshot.Error}";

        IEnumerable<DesktopUiNodeSnapshot> nodes = snapshot.Nodes;
        if (interactiveOnly)
            nodes = nodes.Where(x => DesktopUiControlTypes.IsInteractive(x.ControlType));

        var lines = new List<string>();
        foreach (DesktopUiNodeSnapshot node in nodes)
        {
            string indent = new(' ', node.Depth * 2);
            string name = node.IsProtected ? "[protected]" : node.DisplayName;
            string state = node.IsOffscreen ? " offscreen" : string.Empty;
            lines.Add($"{indent}[{node.ControlType}] {name}{state}");
        }

        if (snapshot.Truncated)
            lines.Add("... tree truncated ...");

        return string.Join(Environment.NewLine, lines);
    }
}
