using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace LuKnight.Services;

public sealed record DesktopWindowTarget(
    nint Handle,
    int ProcessId,
    string ProcessName,
    string Title,
    int ZOrder,
    bool IsMinimized,
    bool IsForeground)
{
    public string Id => $"{ProcessId}:{Handle.ToInt64():X}";

    public string DisplayLabel => string.IsNullOrWhiteSpace(Title)
        ? ProcessName
        : $"{ProcessName} — {Title}";

    public string Fingerprint
    {
        get
        {
            string value = $"{Handle.ToInt64():X}|{ProcessId}|{ProcessName}|{Title}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        }
    }
}

public sealed record DesktopWindowResolution(
    DesktopWindowTarget? Match,
    IReadOnlyList<DesktopWindowTarget> Alternatives)
{
    public bool Found => Match is not null;
    public bool Ambiguous => Match is null && Alternatives.Count > 1;
}

public interface IDesktopWindowTargetCatalog
{
    IReadOnlyList<DesktopWindowTarget> Capture();
    DesktopWindowResolution Resolve(string query);
    bool TryResolveById(string id, out DesktopWindowTarget target);
}

public sealed class DesktopWindowTargetService : IDesktopWindowTargetCatalog
{
    private const int MaximumTitleLength = 512;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowText(nint hwnd, StringBuilder text, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetWindowTextLength(nint hwnd);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    public IReadOnlyList<DesktopWindowTarget> Capture()
    {
        nint foreground = GetForegroundWindow();
        var targets = new List<DesktopWindowTarget>();

        foreach (DesktopWindowInfo window in DesktopWindowService.GetApplicationWindows().OrderBy(x => x.ZOrder))
        {
            if (!DesktopApplicationService.TryGetApplication(window.Handle, out DesktopApplicationContext app))
                continue;
            if (DesktopAppPolicy.IsRestrictedExecutable(app.ProcessName))
                continue;

            string title = ReadTitle(window.Handle);
            if (string.IsNullOrWhiteSpace(title))
                continue;

            targets.Add(new DesktopWindowTarget(
                window.Handle,
                app.ProcessId,
                app.ProcessName,
                title,
                window.ZOrder,
                IsIconic(window.Handle),
                window.Handle == foreground));
        }

        return targets;
    }

    public DesktopWindowResolution Resolve(string query)
    {
        string normalized = Normalize(query);
        if (normalized.Length < 2 || normalized.Length > 200)
            return new(null, Array.Empty<DesktopWindowTarget>());

        IReadOnlyList<DesktopWindowTarget> windows = Capture();
        if (normalized is "aktif" or "active" or "current" or "window aktif" or "current window")
        {
            DesktopWindowTarget? foreground = windows.FirstOrDefault(x => x.IsForeground);
            return foreground is null ? new(null, Array.Empty<DesktopWindowTarget>()) : new(foreground, new[] { foreground });
        }

        string[] tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var scored = windows.Select(window => new { Window = window, Score = Score(window, normalized, tokens) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Window.ZOrder)
            .ToArray();
        if (scored.Length == 0)
            return new(null, Array.Empty<DesktopWindowTarget>());

        int bestScore = scored[0].Score;
        DesktopWindowTarget[] best = scored.Where(x => x.Score == bestScore).Select(x => x.Window).ToArray();
        return best.Length == 1 ? new(best[0], best) : new(null, best);
    }

    public bool TryResolveById(string id, out DesktopWindowTarget target)
    {
        target = default!;
        if (string.IsNullOrWhiteSpace(id))
            return false;

        DesktopWindowTarget? match = Capture().FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
        if (match is null)
            return false;

        target = match;
        return true;
    }

    private static int Score(DesktopWindowTarget window, string query, IReadOnlyList<string> tokens)
    {
        string title = Normalize(window.Title);
        string process = Normalize(window.ProcessName);
        string combined = process + " " + title;
        if (title == query) return 100;
        if (combined == query) return 95;
        if (title.Contains(query, StringComparison.Ordinal)) return 85;
        if (process == query) return 70;
        return tokens.All(token => combined.Contains(token, StringComparison.Ordinal)) ? 60 : 0;
    }

    private static string ReadTitle(nint hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
            return string.Empty;

        int capacity = Math.Min(length, MaximumTitleLength) + 1;
        var builder = new StringBuilder(capacity);
        int copied = GetWindowText(hwnd, builder, capacity);
        return copied <= 0 ? string.Empty : builder.ToString().Trim();
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(' ', value.Trim().ToLowerInvariant()
            .Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
}
