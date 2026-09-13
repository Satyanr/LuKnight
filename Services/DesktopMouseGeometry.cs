using System.Windows;

namespace LuKnight.Services;

public static class DesktopMouseGeometry
{
    public static bool TryGetCenter(Rect bounds, out Point point)
    {
        point = default;
        if (bounds.IsEmpty ||
            bounds.Width < 4 ||
            bounds.Height < 4 ||
            !Finite(bounds.Left) ||
            !Finite(bounds.Top) ||
            !Finite(bounds.Width) ||
            !Finite(bounds.Height))
        {
            return false;
        }

        double x = bounds.Left + bounds.Width / 2.0;
        double y = bounds.Top + bounds.Height / 2.0;
        if (!Finite(x) || !Finite(y))
            return false;

        point = new Point(Math.Round(x), Math.Round(y));
        return true;
    }

    private static bool Finite(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value);
}
