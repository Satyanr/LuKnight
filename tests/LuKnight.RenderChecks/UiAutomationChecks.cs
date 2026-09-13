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
                "0/1", 1, "Edit", "[protected]", "PasswordBox", "Edit",
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
            !formatted.Contains("secret-password", StringComparison.OrdinalIgnoreCase),
            "Protected content leaked from UI snapshot.");

        DesktopUiReadOptions defaults = new();
        Require(
            defaults.MaxDepth == 6 &&
            defaults.MaxNodes == 250 &&
            defaults.EffectiveTimeout == TimeSpan.FromSeconds(4),
            "UI Automation safety limits changed unexpectedly.");

        var fake = new FakeUiAutomationReader { Snapshot = snapshot };
        DesktopUiSnapshot captured = await fake.CaptureAsync(window);
        Require(
            fake.Calls == 1 && captured == snapshot,
            "UI Automation reader abstraction failed.");
    }
}
