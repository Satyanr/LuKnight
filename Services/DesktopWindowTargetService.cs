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
        IReadOnlyList<DesktopWindowInfo> windows = DesktopWindowService.GetApplicationWindows();
        var targets = new List<DesktopWindowTarget>(windows.Count);

        foreach (DesktopWindowInfo window in windows)
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
        return ResolveSnapshot(Capture(), query);
    }

    public static DesktopWindowResolution ResolveSnapshot(
        IReadOnlyList<DesktopWindowTarget> windows,
        string query)
    {
        ArgumentNullException.ThrowIfNull(windows);

        string normalized = Normalize(query);
        if (normalized.Length < 2 || normalized.Length > 200)
            return new(null, Array.Empty<DesktopWindowTarget>());

        if (normalized is "aktif" or "active" or "current" or "foreground" or "window aktif" or "current window")
            return ResolveCurrentWindow(windows);

        string[] tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int bestScore = 0;
        List<DesktopWindowTarget>? best = null;
        for (int i = 0; i < windows.Count; i++)
        {
            DesktopWindowTarget window = windows[i];
            int score = Score(window, normalized, tokens);
            if (score <= 0) continue;
            if (score > bestScore)
            {
                bestScore = score;
                best ??= new List<DesktopWindowTarget>(2);
                best.Clear();
                best.Add(window);
            }
            else if (score == bestScore)
            {
                best!.Add(window);
            }
        }
        if (best is null || best.Count == 0)
            return new(null, Array.Empty<DesktopWindowTarget>());
        if (best.Count == 1)
            return new(best[0], new[] { best[0] });

        best.Sort(static (left, right) => left.ZOrder.CompareTo(right.ZOrder));
        return new(null, best.ToArray());
    }

    private static DesktopWindowResolution ResolveCurrentWindow(IReadOnlyList<DesktopWindowTarget> windows)
    {
        DesktopWindowTarget? foreground = null;
        DesktopWindowTarget? highestVisible = null;
        DesktopWindowTarget? highestAny = null;
        for (int i = 0; i < windows.Count; i++)
        {
            DesktopWindowTarget candidate = windows[i];
            if (candidate.IsForeground)
            {
                foreground = candidate;
                break;
            }
            if (highestAny is null || candidate.ZOrder < highestAny.ZOrder)
                highestAny = candidate;
            if (!candidate.IsMinimized &&
                (highestVisible is null || candidate.ZOrder < highestVisible.ZOrder))
                highestVisible = candidate;
        }
        DesktopWindowTarget? selected = foreground ?? highestVisible ?? highestAny;
        return selected is null
            ? new(null, Array.Empty<DesktopWindowTarget>())
            : new(selected, new[] { selected });
    }

    public bool TryResolveById(string id, out DesktopWindowTarget target)
    {
        target = default!;
        if (string.IsNullOrWhiteSpace(id))
            return false;

        IReadOnlyList<DesktopWindowTarget> windows = Capture();
        for (int i = 0; i < windows.Count; i++)
        {
            DesktopWindowTarget candidate = windows[i];
            if (!string.Equals(candidate.Id, id, StringComparison.Ordinal)) continue;
            target = candidate;
            return true;
        }
        return false;
    }

    private static int Score(DesktopWindowTarget window, string query, IReadOnlyList<string> tokens)
    {
        string title = Normalize(window.Title);
        string process = Normalize(window.ProcessName);
        if (title == query) return 100;
        if (IsCombinedExact(process, title, query)) return 95;
        if (title.Contains(query, StringComparison.Ordinal)) return 85;
        if (process == query) return 70;
        for (int i = 0; i < tokens.Count; i++)
        {
            string token = tokens[i];
            if (!process.Contains(token, StringComparison.Ordinal) &&
                !title.Contains(token, StringComparison.Ordinal))
                return 0;
        }
        return tokens.Count > 0 ? 60 : 0;
    }

    private static bool IsCombinedExact(string process, string title, string query)
    {
        int expectedLength = process.Length + 1 + title.Length;
        return query.Length == expectedLength &&
            query.StartsWith(process, StringComparison.Ordinal) &&
            query[process.Length] == ' ' &&
            query.AsSpan(process.Length + 1).SequenceEqual(title.AsSpan());
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
