using System.Windows;
using Drawing = System.Drawing;

namespace LuKnight.Services;

public static class DesktopUiScreenAssistPolicy
{
    private static readonly HashSet<string>
        AllowedControlTypes =
        new(
            StringComparer.Ordinal)
        {
            "Button",
            "MenuItem",
            "TabItem",
            "CheckBox",
            "RadioButton",
            "Hyperlink",
            "ListItem",
            "TreeItem"
        };

    public static bool ValidateNode(
        DesktopUiNodeSnapshot node,
        out string reason)
    {
        ArgumentNullException.ThrowIfNull(
            node);

        if (node.IsProtected ||
            node.IsPassword)
        {
            reason =
                "Protected UI tidak boleh digunakan sebagai screen evidence.";

            return false;
        }

        if (!node.IsEnabled ||
            node.IsOffscreen)
        {
            reason =
                "UI target tidak terlihat atau tidak aktif.";

            return false;
        }

        if (!AllowedControlTypes.Contains(
                node.ControlType))
        {
            reason =
                "Control type belum diizinkan untuk screen-assisted UIA.";

            return false;
        }

        Rect bounds =
            node.Bounds;

        if (bounds.IsEmpty ||
            !Finite(bounds.Left) ||
            !Finite(bounds.Top) ||
            !Finite(bounds.Width) ||
            !Finite(bounds.Height) ||
            bounds.Width < 4 ||
            bounds.Height < 4 ||
            bounds.Width > 1200 ||
            bounds.Height > 900)
        {
            reason =
                "Bounding rectangle target tidak aman untuk screen evidence.";

            return false;
        }

        reason =
            string.Empty;

        return true;
    }

    private static bool Finite(
        double value) =>
        !double.IsNaN(value) &&
        !double.IsInfinity(value);
}


public static class DesktopUiScreenAssistGeometry
{
    public const int DefaultPadding =
        8;

    public static bool TryGetCaptureBounds(
        Rect controlBounds,
        Drawing.Rectangle windowBounds,
        Drawing.Rectangle virtualScreenBounds,
        out Drawing.Rectangle result,
        int padding =
            DefaultPadding)
    {
        result =
            Drawing.Rectangle.Empty;

        if (padding < 0 ||
            controlBounds.IsEmpty ||
            windowBounds.Width < 1 ||
            windowBounds.Height < 1 ||
            virtualScreenBounds.Width < 1 ||
            virtualScreenBounds.Height < 1)
        {
            return false;
        }

        if (!Finite(controlBounds.Left) ||
            !Finite(controlBounds.Top) ||
            !Finite(controlBounds.Right) ||
            !Finite(controlBounds.Bottom))
        {
            return false;
        }

        double left =
            Math.Max(
                controlBounds.Left -
                    padding,
                Math.Max(
                    windowBounds.Left,
                    virtualScreenBounds.Left));

        double top =
            Math.Max(
                controlBounds.Top -
                    padding,
                Math.Max(
                    windowBounds.Top,
                    virtualScreenBounds.Top));

        double right =
            Math.Min(
                controlBounds.Right +
                    padding,
                Math.Min(
                    windowBounds.Right,
                    virtualScreenBounds.Right));

        double bottom =
            Math.Min(
                controlBounds.Bottom +
                    padding,
                Math.Min(
                    windowBounds.Bottom,
                    virtualScreenBounds.Bottom));

        if (right <= left ||
            bottom <= top)
        {
            return false;
        }

        int x =
            (int)Math.Floor(
                left);

        int y =
            (int)Math.Floor(
                top);

        int rightPixel =
            (int)Math.Ceiling(
                right);

        int bottomPixel =
            (int)Math.Ceiling(
                bottom);

        int width =
            rightPixel - x;

        int height =
            bottomPixel - y;

        if (width < 4 ||
            height < 4)
        {
            return false;
        }

        result =
            new Drawing.Rectangle(
                x,
                y,
                width,
                height);

        return true;
    }

    private static bool Finite(
        double value) =>
        !double.IsNaN(value) &&
        !double.IsInfinity(value);
}


public sealed record DesktopUiScreenEvidence(
    string ControlPath,
    string ControlFingerprint,
    Rect UiBounds,
    Drawing.Rectangle CaptureBounds,
    string MimeType,
    byte[] EncodedBytes,
    int ImageWidth,
    int ImageHeight);
