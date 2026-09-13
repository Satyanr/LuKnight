using System.Windows;
using LuKnight.Services;

internal static partial class Program
{
    private sealed class FakeUiAutomationReader : IDesktopUiAutomationReader
    {
        public int Calls;
        public DesktopUiSnapshot Snapshot = default!;

        public Task<DesktopUiSnapshot> CaptureAsync(
            DesktopWindowTarget window,
            DesktopUiReadOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Snapshot);
        }
    }

    private static async Task CheckUiAutomationAsync()
    {
        DesktopWindowTarget window = new(
            (nint)101,
            10,
            "notepad",
            "Notes",
            0,
            false,
            true);

        DesktopUiNodeSnapshot[] nodes =
        [
            new(
                "0", 0, "Window", "Notes", "", "Notepad",
                new Rect(0, 0, 800, 600),
                true, false, false, false),
            new(
                "0/0", 1, "Edit", "Document", "TextArea", "RichEdit",
                new Rect(10, 50, 700, 500),
                true, false, true, false),
            new(
                "0/1", 1, "Edit", "secret-password-123", "PasswordBox", "SensitiveEdit",
                Rect.Empty,
                true, false, false, true),
            new(
                "0/2", 1, "Button", "Save", "SaveButton", "Button",
                new Rect(700, 10, 80, 30),
                true, false, false, false)
        ];

        var snapshot = new DesktopUiSnapshot(window, nodes, false);
        Require(
            snapshot.Success && snapshot.Nodes.Count == 4,
            "UI Automation snapshot model failed.");
        Require(
            DesktopUiControlTypes.IsInteractive("Button") &&
            DesktopUiControlTypes.IsInteractive("Edit") &&
            !DesktopUiControlTypes.IsInteractive("Window"),
            "Interactive control classification failed.");

        string formatted = DesktopUiSnapshotFormatter.Format(
            snapshot,
            interactiveOnly: true);
        Require(
            formatted.Contains("[Button] Save", StringComparison.Ordinal) &&
            formatted.Contains("[protected]", StringComparison.Ordinal),
            "UI Automation formatter lost control metadata.");
        Require(
            !formatted.Contains("secret-password-123", StringComparison.OrdinalIgnoreCase),
            "Protected content leaked through formatter.");

        DesktopUiReadOptions defaults = new();
        Require(
            defaults.MaxDepth == 6 &&
            defaults.MaxNodes == 250 &&
            defaults.EffectiveTimeout == TimeSpan.FromSeconds(4),
            "UI Automation safety limits changed unexpectedly.");

        var realReader = new WindowsDesktopUiAutomationReader();
        bool badDepthRejected = false;
        try
        {
            await realReader.CaptureAsync(
                window,
                new DesktopUiReadOptions(MaxDepth: 13));
        }
        catch (ArgumentOutOfRangeException)
        {
            badDepthRejected = true;
        }
        Require(badDepthRejected, "UI Automation accepted excessive depth.");

        bool badNodeLimitRejected = false;
        try
        {
            await realReader.CaptureAsync(
                window,
                new DesktopUiReadOptions(MaxNodes: 1001));
        }
        catch (ArgumentOutOfRangeException)
        {
            badNodeLimitRejected = true;
        }
        Require(badNodeLimitRejected, "UI Automation accepted excessive node count.");

        bool badTimeoutRejected = false;
        try
        {
            await realReader.CaptureAsync(
                window,
                new DesktopUiReadOptions(Timeout: TimeSpan.FromSeconds(30)));
        }
        catch (ArgumentOutOfRangeException)
        {
            badTimeoutRejected = true;
        }
        Require(badTimeoutRejected, "UI Automation accepted excessive timeout.");

        var fake = new FakeUiAutomationReader { Snapshot = snapshot };
        DesktopUiSnapshot captured = await fake.CaptureAsync(window);
        Require(
            fake.Calls == 1 && captured == snapshot,
            "UI Automation reader abstraction failed.");
    }
}
