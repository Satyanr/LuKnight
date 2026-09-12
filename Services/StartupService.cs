using System.IO;
using Microsoft.Win32;

namespace LuKnight.Services;

public interface IStartupStore
{
    string? ReadCommand();
    void WriteCommand(string command);
    void DeleteCommand();
    bool ReadStartHidden();
    void WriteStartHidden(bool hidden);
}

public sealed class RegistryStartupStore : IStartupStore
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string PreferenceKey = @"Software\LuKnight";
    public string? ReadCommand() { using var key = Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("LuKnight") as string; }
    public void WriteCommand(string command) { using var key = Registry.CurrentUser.CreateSubKey(RunKey, true); key.SetValue("LuKnight", command, RegistryValueKind.String); }
    public void DeleteCommand() { using var key = Registry.CurrentUser.OpenSubKey(RunKey, true); key?.DeleteValue("LuKnight", false); }
    public bool ReadStartHidden() { using var key = Registry.CurrentUser.OpenSubKey(PreferenceKey); return key?.GetValue("StartHidden") is int value && value == 1; }
    public void WriteStartHidden(bool hidden) { using var key = Registry.CurrentUser.CreateSubKey(PreferenceKey, true); key.SetValue("StartHidden", hidden ? 1 : 0, RegistryValueKind.DWord); }
}

public sealed record StartupStatus(bool Enabled, bool StartHidden, bool Available, string Message);

public sealed class StartupService
{
    private readonly IStartupStore _store;
    private readonly string _executable, _assembly;
    private readonly Func<string, bool> _exists;
    public StartupService() : this(new RegistryStartupStore(), Environment.ProcessPath ?? "", typeof(StartupService).Assembly.Location, File.Exists) { }
    public StartupService(IStartupStore store, string executable, string assembly, Func<string, bool> exists)
    { _store = store; _executable = executable; _assembly = assembly; _exists = exists; }

    public static string BuildCommand(string executable, string assembly)
    {
        static string Quote(string path)
        {
            if (!Path.IsPathFullyQualified(path) || path.IndexOfAny(['"', '\r', '\n']) >= 0)
                throw new InvalidOperationException("Lokasi aplikasi untuk startup tidak valid.");
            return "\"" + path + "\"";
        }
        string command = Quote(executable);
        if (IsDotnet(executable)) command += " " + Quote(assembly);
        command += " --startup";
        if (command.Length > 260) throw new InvalidOperationException("Lokasi aplikasi terlalu panjang untuk startup Windows. Pindahkan Lu-Knight ke folder yang lebih pendek.");
        return command;
    }
    private static bool IsDotnet(string path) => Path.GetFileNameWithoutExtension(path).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
    private string CheckedCommand()
    {
        string command = BuildCommand(_executable, _assembly);
        if (!_exists(_executable) || (IsDotnet(_executable) && !_exists(_assembly)))
            throw new InvalidOperationException("File aplikasi untuk startup tidak ditemukan.");
        return command;
    }
    public bool IsEnabled() => !string.IsNullOrWhiteSpace(_store.ReadCommand());
    public void Enable() => _store.WriteCommand(CheckedCommand());
    public void Disable() => _store.DeleteCommand();
    public void SetStartHidden(bool hidden) => _store.WriteStartHidden(hidden);

    // Repairs only an existing opt-in entry when this version is launched from its new location.
    public bool Validate()
    {
        var saved = _store.ReadCommand();
        if (string.IsNullOrWhiteSpace(saved)) return false;
        string expected = CheckedCommand();
        if (string.Equals(saved, expected, StringComparison.Ordinal)) return false;
        _store.WriteCommand(expected);
        return true;
    }
    public StartupStatus ReadStatus()
    {
        try
        {
            bool enabled = IsEnabled();
            return new(enabled, _store.ReadStartHidden(), true,
                enabled ? "Aktif untuk akun Windows ini." : "Nonaktif. Lu-Knight dibuka secara manual.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        { return new(false, false, false, "Pengaturan startup tidak dapat dibaca. Periksa izin akun Windows."); }
    }
    public static bool ShouldStartHidden(IEnumerable<string> arguments, bool preference, bool trayAvailable) =>
        trayAvailable && preference && arguments.Contains("--startup", StringComparer.OrdinalIgnoreCase);
}
