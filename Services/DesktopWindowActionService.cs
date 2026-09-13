using System.Runtime.InteropServices;

namespace LuKnight.Services;

public interface IDesktopWindowActionExecutor
{
    DesktopActionResult Focus(DesktopWindowTarget target);
}

public sealed class WindowsDesktopWindowActionExecutor : IDesktopWindowActionExecutor
{
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(nint hwnd, int command);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint hwnd);

    public DesktopActionResult Focus(DesktopWindowTarget target)
    {
        if (target.Handle == nint.Zero || !IsWindow(target.Handle))
            return new(false, "Window target sudah tidak tersedia.");

        if (target.IsMinimized)
            ShowWindowAsync(target.Handle, SwRestore);

        bool focused = SetForegroundWindow(target.Handle);
        return focused
            ? new(true, $"Window {target.DisplayLabel} difokuskan.")
            : new(false, $"Windows tidak mengizinkan fokus ke {target.DisplayLabel} saat ini.");
    }
}
