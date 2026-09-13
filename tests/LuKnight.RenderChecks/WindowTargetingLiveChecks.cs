using LuKnight.Services;

internal static partial class Program
{
    private static void CheckWindowTargetingLive()
    {
        var catalog = new DesktopWindowTargetService();
        IReadOnlyList<DesktopWindowTarget> windows = catalog.Capture();

        Console.WriteLine();
        Console.WriteLine($"Visible application windows: {windows.Count}");
        Console.WriteLine(new string('-', 80));

        foreach (DesktopWindowTarget window in windows)
        {
            string state = window.IsForeground
                ? "FOREGROUND"
                : window.IsMinimized
                    ? "MINIMIZED"
                    : "VISIBLE";

            Console.WriteLine($"[{state}] {window.ProcessName} | PID {window.ProcessId}");
            Console.WriteLine($"  {window.Title}");
            Console.WriteLine($"  ID: {window.Id}");
            Console.WriteLine();
        }

        Require(
            windows.All(x => x.Handle != nint.Zero),
            "Live inventory returned zero HWND.");
        Require(
            windows.All(x => x.ProcessId > 0),
            "Live inventory returned invalid PID.");
        Require(
            windows.All(x => !string.IsNullOrWhiteSpace(x.ProcessName)),
            "Live inventory returned empty process name.");
        Require(
            windows.All(x => !string.IsNullOrWhiteSpace(x.Title)),
            "Live inventory returned empty title.");
        Require(
            windows.All(x => x.ProcessId != Environment.ProcessId),
            "Lu-Knight/test process leaked into window targets.");
        Require(
            windows.All(x => !DesktopAppPolicy.IsRestrictedExecutable(x.ProcessName)),
            "Restricted executable leaked into window targets.");

        string[] ids = windows.Select(x => x.Id).ToArray();
        Require(
            ids.Distinct(StringComparer.Ordinal).Count() == ids.Length,
            "Live window inventory contains duplicate IDs.");

        DesktopWindowResolution current =
            DesktopWindowTargetService.ResolveSnapshot(windows, "window aktif");

        if (windows.Count == 0)
        {
            Require(
                !current.Found,
                "Current-window resolver returned a target from an empty inventory.");
        }
        else
        {
            Require(
                current.Found && current.Match is not null,
                "Current external window could not be resolved.");
            DesktopWindowTarget match = current.Match!;
            Console.WriteLine("Current external target:");
            Console.WriteLine($"  {match.DisplayLabel}");
        }

        int foregroundCount = windows.Count(x => x.IsForeground);
        Require(
            foregroundCount <= 1,
            "More than one foreground window was reported.");

        Console.WriteLine();
        Console.WriteLine("PASS: live window inventory is structurally valid.");
        Console.WriteLine("No window was focused or modified by this diagnostic.");
    }
}
