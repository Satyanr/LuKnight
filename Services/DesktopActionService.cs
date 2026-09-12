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

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint hwnd);

    public DesktopActionResult Open(DesktopAppTarget app)
    {
        if (!WindowsDesktopAppDiscovery.IsLaunchUnchanged(app))
            return new(false, "Target aplikasi berubah atau tidak diizinkan. Segarkan daftar aplikasi dan ulangi perintah.");
        if (string.IsNullOrWhiteSpace(app.LaunchTarget))
            return new DesktopActionResult(false, $"{app.DisplayName} belum mendukung launch.");

        try
        {
            if (app.Source == DesktopAppSource.AppsFolder)
            {
                using Process? activation = Process.Start(new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    Arguments = "shell:AppsFolder\\" + app.AppUserModelId,
                    UseShellExecute = true
                });
                return new(true, $"{app.DisplayName} dibuka.");
            }
            using Process? process = Process.Start(new ProcessStartInfo
            {
                // Launch the inspected executable, not a shortcut which can change after validation.
                FileName = app.ResolvedExecutable ?? app.LaunchTarget,
                Arguments = app.Arguments,
                WorkingDirectory = Directory.Exists(app.WorkingDirectory) ? app.WorkingDirectory : Environment.SystemDirectory,
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
        if (!DesktopAppPolicy.IsAllowed(app)) return new(false, "Target aplikasi tidak diizinkan.");
        IReadOnlyList<DesktopWindowInfo> windows = DesktopWindowService.GetApplicationWindows();

        DesktopWindowInfo? match = null;
        foreach (DesktopWindowInfo window in windows)
        {
            if (!DesktopApplicationService.TryGetApplication(window.Handle, out DesktopApplicationContext application))
                continue;

            if (MatchesProcess(app, application.ProcessName))
            {
                match = window;
                break;
            }
        }

        if (match is null || match.Value.Handle == nint.Zero)
            return new DesktopActionResult(false, $"{app.DisplayName} tidak sedang memiliki window yang dapat difokuskan.");

        nint handle = match.Value.Handle;
        if (IsIconic(handle))
            ShowWindowAsync(handle, SwRestore);
        bool focused = SetForegroundWindow(handle);

        return focused
            ? new DesktopActionResult(true, $"{app.DisplayName} difokuskan.")
            : new DesktopActionResult(false, $"Windows tidak mengizinkan fokus ke {app.DisplayName} saat ini.");
    }

    public static bool MatchesProcess(DesktopAppTarget app, string processName)
    {
        if (DesktopAppPolicy.IsRestrictedExecutable(processName)) return false;
        if (app.ProcessNames.Any(p => p.Equals(processName, StringComparison.OrdinalIgnoreCase))) return true;
        if (app.Source == DesktopAppSource.AppsFolder) return false;
        string process = DesktopNameNormalizer.Normalize(processName);
        return process.Length > 0 && app.Aliases.Any(alias =>
            DesktopNameNormalizer.Normalize(alias).Split(' ').Contains(process, StringComparer.OrdinalIgnoreCase));
    }
}
