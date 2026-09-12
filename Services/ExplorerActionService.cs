using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace LuKnight.Services;

public interface IExplorerActionExecutor
{
    DesktopActionResult OpenFolder(string path);
    DesktopActionResult Search(string query, string? path);
}

public sealed class WindowsExplorerActionExecutor : IExplorerActionExecutor
{
    private readonly Action<ProcessStartInfo> _launch;
    public WindowsExplorerActionExecutor(Action<ProcessStartInfo>? launch = null) =>
        _launch = launch ?? (info => { using var process = Process.Start(info); });

    public static bool IsValidQuery(string query) => !string.IsNullOrWhiteSpace(query) &&
        query.Trim().Length is >= 2 and <= 200 && !query.Any(char.IsControl);

    public DesktopActionResult OpenFolder(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Directory.Exists(path)) return new(false, "Folder tidak ditemukan.");
        return Launch(new ProcessStartInfo(path) { UseShellExecute = true }, "File Explorer dibuka.");
    }

    public DesktopActionResult Search(string query, string? path)
    {
        if (!IsValidQuery(query)) return new(false, "Query pencarian tidak valid.");
        if (path is not null && (!Path.IsPathFullyQualified(path) || !Directory.Exists(path)))
            return new(false, "Folder pencarian tidak ditemukan.");
        string uri = BuildSearchUri(query.Trim(), path);
        return Launch(new ProcessStartInfo(uri) { UseShellExecute = true }, $"Membuka pencarian Explorer untuk \"{query.Trim()}\".");
    }

    public static string BuildSearchUri(string query, string? path) => "search-ms:query=" + Uri.EscapeDataString(query) +
        (path is null ? "" : "&crumb=location:" + Uri.EscapeDataString(path));

    private DesktopActionResult Launch(ProcessStartInfo info, string message)
    {
        try { _launch(info); return new(true, message); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        { return new(false, "File Explorer tidak dapat dibuka saat ini."); }
    }
}
