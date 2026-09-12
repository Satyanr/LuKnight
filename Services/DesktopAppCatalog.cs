namespace LuKnight.Services;

public sealed record DesktopAppTarget(
    string Id,
    string DisplayName,
    string? LaunchTarget,
    IReadOnlyList<string> ProcessNames);

public static class DesktopAppCatalog
{
    private static readonly DesktopAppTarget[] Targets =
    [
        new("notepad", "Notepad", "notepad.exe", ["notepad"]),
        new("calculator", "Calculator", "calc.exe", ["CalculatorApp", "Calculator"]),
        new("explorer", "File Explorer", "explorer.exe", ["explorer"]),
        new("settings", "Windows Settings", "ms-settings:", ["SystemSettings"]),
        new("chrome", "Google Chrome", "chrome.exe", ["chrome"]),
        new("edge", "Microsoft Edge", "msedge.exe", ["msedge"]),
        new("firefox", "Mozilla Firefox", "firefox.exe", ["firefox"]),
        new("vscode", "Visual Studio Code", "Code.exe", ["code"]),
        new("excel", "Microsoft Excel", "excel.exe", ["excel"]),
        new("word", "Microsoft Word", "winword.exe", ["winword"]),
        new("powerpoint", "Microsoft PowerPoint", "powerpnt.exe", ["powerpnt"])
    ];

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["notepad"] = "notepad",
        ["notes"] = "notepad",
        ["calculator"] = "calculator",
        ["kalkulator"] = "calculator",
        ["explorer"] = "explorer",
        ["file explorer"] = "explorer",
        ["windows explorer"] = "explorer",
        ["settings"] = "settings",
        ["pengaturan"] = "settings",
        ["windows settings"] = "settings",
        ["chrome"] = "chrome",
        ["google chrome"] = "chrome",
        ["edge"] = "edge",
        ["microsoft edge"] = "edge",
        ["firefox"] = "firefox",
        ["vscode"] = "vscode",
        ["vs code"] = "vscode",
        ["visual studio code"] = "vscode",
        ["excel"] = "excel",
        ["microsoft excel"] = "excel",
        ["word"] = "word",
        ["microsoft word"] = "word",
        ["powerpoint"] = "powerpoint",
        ["microsoft powerpoint"] = "powerpoint"
    };

    public static bool TryResolve(string value, out DesktopAppTarget target)
    {
        target = default!;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string alias = value.Trim();

        if (!Aliases.TryGetValue(alias, out string? id))
            return false;

        return TryResolveById(id, out target);
    }

    public static bool TryResolveById(string id, out DesktopAppTarget target)
    {
        target = Targets.FirstOrDefault(item =>
            string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))!;

        return target is not null;
    }
}
