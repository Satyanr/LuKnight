using System.IO;
using System.Text.RegularExpressions;

namespace LuKnight.Services;

public static class DesktopAppPolicy
{
    private static readonly HashSet<string> Restricted = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32", "regsvr32", "regedit",
        "schtasks", "diskpart", "bcdedit", "wt", "windowsterminal", "mmc", "msiexec", "wsl", "bash", "sh",
        "python", "pythonw", "py", "node", "csi", "installutil", "msbuild", "shutdown", "reg", "conhost"
    };
    private static readonly string[] RestrictedNames =
        ["command prompt", "powershell", "windows terminal", "registry editor"];

    public static bool IsRestrictedQuery(string query)
    {
        // Validate before punctuation normalization: paths/commands must not become fuzzy app aliases.
        if (query.IndexOfAny(['\\', '/', ':', ';', '|', '&', '\r', '\n', '`', '$', '<', '>']) >= 0) return true;
        string value = DesktopNameNormalizer.Normalize(query);
        return RestrictedNames.Any(value.Contains) || value.Split(' ').Any(Restricted.Contains) ||
            Regex.IsMatch(query, @"\.(exe|lnk|bat|cmd|ps1|vbs|js|msi)\b", RegexOptions.IgnoreCase);
    }

    public static bool IsAllowed(DesktopAppTarget app)
    {
        if (RestrictedNames.Any(name => DesktopNameNormalizer.Normalize(app.DisplayName).Contains(name, StringComparison.Ordinal))) return false;
        if (app.ProcessNames.Any(IsRestrictedExecutable)) return false;
        if (DesktopNameNormalizer.Normalize(app.Arguments).Split(' ').Any(Restricted.Contains)) return false;
        string executable = app.ResolvedExecutable ?? app.LaunchTarget;
        if (app.Source == DesktopAppSource.BuiltIn && app.Id == "settings" && executable == "ms-settings:") return true;
        if (!string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase) || IsRestrictedExecutable(executable)) return false;
        if (app.Source != DesktopAppSource.BuiltIn && !IsLocalExecutable(executable)) return false;
        if (app.Source == DesktopAppSource.StartMenu && string.IsNullOrWhiteSpace(app.ResolvedExecutable)) return false;
        if (Path.GetFileNameWithoutExtension(executable).Equals("explorer", StringComparison.OrdinalIgnoreCase) && app.Arguments.Length > 0) return false;
        return !Regex.IsMatch(DesktopNameNormalizer.Normalize(app.DisplayName), @"\b(uninstall|uninstaller|remove|repair|setup|installer)\b");
    }

    public static bool IsRestrictedExecutable(string value) => Restricted.Contains(Path.GetFileNameWithoutExtension(value));
    public static bool IsLocalExecutable(string path) => Path.IsPathFullyQualified(path) &&
        !path.StartsWith(@"\\", StringComparison.Ordinal) && path.IndexOf(':', 2) < 0 &&
        Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
}
