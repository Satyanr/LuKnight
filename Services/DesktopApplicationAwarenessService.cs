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

        var applications = new List<DesktopApplicationContext>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (DesktopWindowInfo window in DesktopWindowService
            .GetVisibleWindows()
            .OrderBy(window => window.ZOrder))
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

        DesktopApplicationContext? primary = applications.Count > 0
            ? applications[0]
            : null;

        return new DesktopApplicationSnapshot(
            primary,
            applications.ToArray());
    }

    public static string Format(DesktopApplicationContext application)
    {
        return $"{application.ProcessName} ({application.Kind})";
    }
}
