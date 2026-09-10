using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace LuKnight.Services;

public static class DesktopCursorService
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(
        out NativePoint point);

    public static bool TryGetPositionDip(
        Visual relativeTo,
        out Point position)
    {
        if (!TryGetPosition(
                out Point physicalPosition))
        {
            position = default;
            return false;
        }

        PresentationSource? source =
            PresentationSource.FromVisual(
                relativeTo);

        if (source?.CompositionTarget is null)
        {
            position = physicalPosition;
            return true;
        }

        Matrix fromDevice =
            source.CompositionTarget
                .TransformFromDevice;

        position =
            fromDevice.Transform(
                physicalPosition);

        return true;
    }

    public static bool TryGetPosition(
        out Point position)
    {
        if (GetCursorPos(out NativePoint point))
        {
            position =
                new Point(
                    point.X,
                    point.Y);

            return true;
        }

        position = default;
        return false;
    }
}