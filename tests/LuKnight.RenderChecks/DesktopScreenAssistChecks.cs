using System.Windows;
using LuKnight.Services;
using Drawing = System.Drawing;

internal static partial class Program
{
    private static void
        CheckDesktopScreenAssist()
    {
        var safe =
            new DesktopUiNodeSnapshot(
                "0/1",
                1,
                "Button",
                "Refresh",
                "RefreshButton",
                "Button",
                new Rect(
                    100,
                    200,
                    80,
                    40),
                true,
                false,
                false,
                false);

        Require(
            DesktopUiScreenAssistPolicy
                .ValidateNode(
                    safe,
                    out _),
            "Safe Button was rejected by screen-assist policy.");

        foreach (
            DesktopUiNodeSnapshot blocked
            in new[]
            {
                safe with
                {
                    ControlType =
                        "Edit"
                },

                safe with
                {
                    IsPassword =
                        true
                },

                safe with
                {
                    IsEnabled =
                        false
                },

                safe with
                {
                    IsOffscreen =
                        true
                },

                safe with
                {
                    Bounds =
                        Rect.Empty
                },

                safe with
                {
                    Bounds =
                        new Rect(
                            0,
                            0,
                            2000,
                            1000)
                }
            })
        {
            Require(
                !DesktopUiScreenAssistPolicy
                    .ValidateNode(
                        blocked,
                        out _),
                "Unsafe screen-assist target was accepted.");
        }

        var window =
            new Drawing.Rectangle(
                50,
                100,
                500,
                500);

        var virtualScreen =
            new Drawing.Rectangle(
                -1920,
                0,
                3840,
                1080);

        Require(
            DesktopUiScreenAssistGeometry
                .TryGetCaptureBounds(
                    safe.Bounds,
                    window,
                    virtualScreen,
                    out Drawing.Rectangle
                        region),
            "Safe screen-assist region was rejected.");

        Require(
            region ==
                new Drawing.Rectangle(
                    92,
                    192,
                    96,
                    56),
            $"Unexpected capture bounds: {region}.");

        var edge =
            safe with
            {
                Bounds =
                    new Rect(
                        52,
                        102,
                        40,
                        30)
            };

        Require(
            DesktopUiScreenAssistGeometry
                .TryGetCaptureBounds(
                    edge.Bounds,
                    window,
                    virtualScreen,
                    out region),
            "Edge region was rejected.");

        Require(
            region.Left ==
                window.Left &&
            region.Top ==
                window.Top,
            "Screen evidence escaped window bounds.");

        Require(
            !DesktopUiScreenAssistGeometry
                .TryGetCaptureBounds(
                    new Rect(
                        900,
                        900,
                        30,
                        30),
                    window,
                    virtualScreen,
                    out _),
            "Control outside target window became capturable.");

        Require(
            !DesktopUiScreenAssistPolicy
                .ValidateNode(
                    safe with
                    {
                        ControlType =
                            "Edit",
                        Name =
                            "Password",
                        IsPassword =
                            true
                    },
                    out _),
            "Protected text field entered visual evidence path.");

        foreach (string type in new[] { "MenuItem", "TabItem", "CheckBox", "RadioButton", "Hyperlink", "ListItem", "TreeItem" })
            Require(DesktopUiScreenAssistPolicy.ValidateNode(safe with { ControlType = type }, out _),
                $"Allowed screen-assist type rejected: {type}");
        foreach (Rect invalid in new[]
        {
            new Rect(0, 0, 3, 40), new Rect(0, 0, 80, 3),
            new Rect(double.NaN, 0, 80, 40), new Rect(double.PositiveInfinity, 0, 80, 40)
        })
            Require(!DesktopUiScreenAssistPolicy.ValidateNode(safe with { Bounds = invalid }, out _),
                "Invalid screen-assist bounds accepted.");
        Require(DesktopUiScreenAssistGeometry.TryGetCaptureBounds(
            new Rect(-1918, 2, 40, 30), new Drawing.Rectangle(-1920, 0, 500, 500), virtualScreen, out region) &&
            region == new Drawing.Rectangle(-1920, 0, 50, 40),
            "Negative-monitor coordinates or virtual-screen clipping failed.");
        Require(!DesktopUiScreenAssistGeometry.TryGetCaptureBounds(safe.Bounds, window, virtualScreen, out _, -1),
            "Negative capture padding accepted.");
        Require(!DesktopUiScreenAssistGeometry.TryGetCaptureBounds(Rect.Empty, window, virtualScreen, out _),
            "Empty UI bounds accepted.");
        Require(!DesktopUiScreenAssistGeometry.TryGetCaptureBounds(safe.Bounds, Drawing.Rectangle.Empty, virtualScreen, out _),
            "Empty window bounds accepted.");
        Require(ScreenCaptureService.CaptureRegion(Drawing.Rectangle.Empty) is null,
            "Empty capture region accepted.");
        Require(ScreenCaptureService.CaptureRegion(new Drawing.Rectangle(0, 0, 10, 10), maxWidth: 0) is null,
            "Invalid capture dimensions accepted.");
        Require(ScreenCaptureService.CaptureRegion(new Drawing.Rectangle(0, 0, 10, 10), maxEncodedBytes: 100) is null,
            "Invalid encoded-byte limit accepted.");
    }
}
