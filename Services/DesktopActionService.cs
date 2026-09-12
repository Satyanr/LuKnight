using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace LuKnight.Services;

public sealed record DesktopActionResult(
    bool Success,
    string Message);

public interface IDesktopActionExecutor
{
    DesktopActionResult Open(DesktopAppTarget app);
    DesktopActionResult Focus(DesktopAppTarget app);
}

public sealed class WindowsDesktopActionExecutor : IDesktopActionExecutor
{
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindowAsync(nint hwnd, int command);

    public DesktopActionResult Open(DesktopAppTarget app)
    {
        if (string.IsNullOrWhiteSpace(app.LaunchTarget))
            return new DesktopActionResult(false, $"{app.DisplayName} belum mendukung launch.");

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = app.LaunchTarget,
                UseShellExecute = true
            });

            return new DesktopActionResult(true, $"{app.DisplayName} dibuka.");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return new DesktopActionResult(false,
                $"Aku tidak dapat membuka {app.DisplayName}. Aplikasi mungkin belum terpasang atau tidak terdaftar di Windows.");
        }
    }

    public DesktopActionResult Focus(DesktopAppTarget app)
    {
        IReadOnlyList<DesktopWindowInfo> windows = DesktopWindowService.GetVisibleWindows();

        DesktopWindowInfo? match = null;
        foreach (DesktopWindowInfo window in windows)
        {
            if (!DesktopApplicationService.TryGetApplication(window.Handle, out DesktopApplicationContext application))
                continue;

            if (app.ProcessNames.Any(process =>
                string.Equals(process, application.ProcessName, StringComparison.OrdinalIgnoreCase)))
            {
                match = window;
                break;
            }
        }

        if (match is null || match.Value.Handle == nint.Zero)
            return new DesktopActionResult(false, $"{app.DisplayName} tidak sedang memiliki window yang terlihat.");

        nint handle = match.Value.Handle;
        ShowWindowAsync(handle, SwRestore);
        bool focused = SetForegroundWindow(handle);

        return focused
            ? new DesktopActionResult(true, $"{app.DisplayName} difokuskan.")
            : new DesktopActionResult(false, $"Windows tidak mengizinkan fokus ke {app.DisplayName} saat ini.");
    }
}
