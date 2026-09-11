using System.Windows;
using System.Windows.Media;
using LuKnight.Views;
using LuKnight.Visuals;

namespace LuKnight.Services;

public static class CharacterGrounding
{
    // Registered sole baseline shared by the idle bitmap and the walking rig.
    public const double SpriteSoleY = 638;
    public static double FootOffsetInPixels(double viewTop, double viewHeight, double dpiScale) =>
        (viewTop + viewHeight * SpriteSoleY / 660) * dpiScale;

    public static double GetFootOffset(Window window, CharacterView character, Rect windowBounds)
    {
        if (character.RenderMode != CharacterRenderMode.Sprite) return windowBounds.Height;
        double dpi = VisualTreeHelper.GetDpi(window).DpiScaleY;
        double height = character.ActualHeight > 0 ? character.ActualHeight : character.Height;
        double viewTop = Window.GetWindow(character) == window
            ? character.TranslatePoint(new Point(0, 0), window).Y
            : (windowBounds.Height / dpi - height) / 2;
        return Math.Clamp(FootOffsetInPixels(viewTop, height, dpi), 0, windowBounds.Height);
    }

    public static Rect CollisionBounds(Window window, CharacterView character, Rect bounds) =>
        new(bounds.Left, bounds.Top, bounds.Width, GetFootOffset(window, character, bounds));
}
