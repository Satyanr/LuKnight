using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class InspectDesktopUiTool : IAssistantTool
{
    private readonly Func<bool> _enabled;
    private readonly IDesktopWindowTargetCatalog _windows;
    private readonly IDesktopUiAutomationReader _ui;

    public string Name => BuiltInToolNames.DesktopInspectUi;

    public InspectDesktopUiTool(
        Func<bool> enabled,
        IDesktopWindowTargetCatalog windows,
        IDesktopUiAutomationReader ui)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
    }

    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_enabled())
            return new(false, "Desktop interaction sedang nonaktif.");

        if (!TryArgument(invocation, "window", out string windowQuery) ||
            string.IsNullOrWhiteSpace(windowQuery))
        {
            return new(false, "Window target tidak disebutkan.");
        }

        DesktopWindowResolution window = _windows.Resolve(windowQuery);
        if (window.Ambiguous)
        {
            string alternatives = string.Join(
                ", ",
                window.Alternatives.Take(4).Select(x => x.DisplayLabel));
            return new(
                false,
                $"Ada beberapa window yang cocok: {alternatives}. Sebutkan window yang lebih spesifik.");
        }

        if (!window.Found || window.Match is null)
            return new(false, $"Aku tidak menemukan window \"{windowQuery}\".");

        DesktopUiSnapshot snapshot = await _ui.CaptureAsync(
            window.Match,
            new DesktopUiReadOptions(
                MaxDepth: 6,
                MaxNodes: 250,
                MaxTextLength: 160,
                Timeout: TimeSpan.FromSeconds(4)),
            cancellationToken);

        if (!snapshot.Success)
        {
            return new(
                false,
                snapshot.Error ?? "UI Automation tidak dapat membaca window.");
        }

        TryArgument(invocation, "type", out string controlType);
        TryArgument(invocation, "query", out string query);
        TryArgument(invocation, "mode", out string mode);

        if (mode.Equals(nameof(DesktopUiQueryMode.List), StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<DesktopUiNodeSnapshot> controls =
                DesktopUiControlResolver.List(snapshot, controlType, maximum: 12);
            if (controls.Count == 0)
                return new(true, $"Tidak ada {DescribeType(controlType)} yang terlihat di window tersebut.");

            string items = string.Join(
                Environment.NewLine,
                controls.Select((x, index) => $"{index + 1}. {Format(x)}"));
            return new(true, $"{DescribeType(controlType)} yang ditemukan:\n{items}");
        }

        DesktopUiControlResolution result =
            DesktopUiControlResolver.Resolve(snapshot, query, controlType);
        if (result.Found && result.Match is { } match)
            return new(true, $"Ditemukan {Format(match)}.");

        if (result.Ambiguous)
        {
            string alternatives = string.Join(
                Environment.NewLine,
                result.Alternatives.Take(6)
                    .Select((x, index) => $"{index + 1}. {Format(x)}"));
            return new(true, $"Ada beberapa control yang cocok:\n{alternatives}");
        }

        return new(
            true,
            $"Aku tidak menemukan {DescribeType(controlType)} bernama \"{query}\" di window tersebut.");
    }

    private static string Format(DesktopUiNodeSnapshot node)
    {
        string state = node.IsOffscreen
            ? " · offscreen"
            : !node.IsEnabled
                ? " · disabled"
                : node.HasKeyboardFocus
                    ? " · focused"
                    : string.Empty;
        string name = string.IsNullOrWhiteSpace(node.DisplayName)
            ? "(tanpa nama)"
            : node.DisplayName;
        return $"[{node.ControlType}] {name}{state}";
    }

    private static string DescribeType(string? controlType)
    {
        return DesktopUiControlResolver.NormalizeType(controlType) switch
        {
            "Button" => "tombol",
            "Edit" => "textbox",
            "Menu" => "menu",
            "List" => "list",
            "Tab" => "tab",
            "ComboBox" => "dropdown",
            "CheckBox" => "checkbox",
            "RadioButton" => "radio button",
            "Hyperlink" => "link",
            _ => "control"
        };
    }

    private static bool TryArgument(
        ToolInvocation invocation,
        string name,
        out string value)
    {
        if (invocation.Arguments.TryGetValue(name, out string? found))
        {
            value = found?.Trim() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }
}
