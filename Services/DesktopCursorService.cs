using System.Runtime.InteropServices;
using System.Windows;

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