namespace LuKnight.Services;

public sealed record DesktopApplicationSnapshot(
    DesktopApplicationContext? Primary,
    IReadOnlyList<DesktopApplicationContext> Applications)
{
    public bool HasApplications => Applications.Count > 0;
}

public static class DesktopApplicationAwarenessService
{
    public static DesktopApplicationSnapshot Capture(
        int maxApplications = 8)
    {
        if (maxApplications <= 0)
        {
            return new DesktopApplicationSnapshot(
                null,
                Array.Empty<DesktopApplicationContext>());
        }

        IReadOnlyList<DesktopWindowInfo> windows = DesktopWindowService.GetVisibleWindows();
        int capacity = Math.Min(maxApplications, windows.Count);
        var applications = new List<DesktopApplicationContext>(capacity);
        var seen = new HashSet<string>(capacity, StringComparer.OrdinalIgnoreCase);

        // Native enumeration already supplies Z-order.
        foreach (DesktopWindowInfo window in windows)
        {
            if (!DesktopApplicationService.TryGetApplication(
                window.Handle,
                out DesktopApplicationContext application))
            {
                continue;
            }

            if (!seen.Add(application.ProcessName))
            {
                continue;
            }

            applications.Add(application);

            if (applications.Count >= maxApplications)
            {
                break;
            }
        }

        if (applications.Count == 0)
            return new(null, Array.Empty<DesktopApplicationContext>());

        DesktopApplicationContext[] snapshot = applications.ToArray();
        return new(snapshot[0], snapshot);
    }

    public static string[] FormatApplications(IReadOnlyList<DesktopApplicationContext> applications)
    {
        ArgumentNullException.ThrowIfNull(applications);
        if (applications.Count == 0) return Array.Empty<string>();
        var formatted = new string[applications.Count];
        for (int i = 0; i < applications.Count; i++)
            formatted[i] = Format(applications[i]);
        return formatted;
    }

    public static string Format(DesktopApplicationContext application)
    {
        return $"{application.ProcessName} ({application.Kind})";
    }
}
