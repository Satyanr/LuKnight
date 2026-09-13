using LuKnight.Services;

internal static partial class Program
{
    private static async Task CheckUiAutomationLiveAsync()
    {
        var windows = new DesktopWindowTargetService();
        IReadOnlyList<DesktopWindowTarget> inventory = windows.Capture();
        DesktopWindowResolution current =
            DesktopWindowTargetService.ResolveSnapshot(inventory, "window aktif");

        Require(
            current.Match is not null,
            "Tidak ada external window untuk UIA live test.");

        DesktopWindowTarget target = current.Match!;
        var reader = new WindowsDesktopUiAutomationReader();
        DesktopUiSnapshot snapshot = await reader.CaptureAsync(
            target,
            new DesktopUiReadOptions(
                MaxDepth: 6,
                MaxNodes: 250,
                MaxTextLength: 160,
                Timeout: TimeSpan.FromSeconds(4)));

        Require(snapshot.Success, snapshot.Error ?? "UI Automation capture failed.");

        Console.WriteLine();
        Console.WriteLine($"Window: {target.DisplayLabel}");
        Console.WriteLine($"Nodes: {snapshot.Nodes.Count}");
        Console.WriteLine($"Truncated: {snapshot.Truncated}");
        Console.WriteLine();
        Console.WriteLine(DesktopUiSnapshotFormatter.Format(snapshot, interactiveOnly: true));

        Require(
            snapshot.Nodes.All(x =>
                !x.IsPassword ||
                (x.Name == "[protected]" &&
                 string.IsNullOrEmpty(x.AutomationId) &&
                 string.IsNullOrEmpty(x.ClassName))),
            "Protected UI Automation metadata was exposed.");

        Console.WriteLine();
        Console.WriteLine("PASS: read-only UI Automation tree captured.");
        Console.WriteLine("No Invoke/Value/Text/action pattern was requested.");
    }
}
