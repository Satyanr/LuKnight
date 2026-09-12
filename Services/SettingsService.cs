using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using LuKnight.Models;

namespace LuKnight.Services;

public sealed class SettingsService
{
    public static string UserDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LuKnight");
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true,
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }, IgnoreReadOnlyProperties = true
    };
    private readonly string? _path;
    private bool _readOnly;
    public AppSettings Current { get; private set; } = new();
    public string Status { get; private set; } = "";
    public bool IsPersistent => _path is not null;
    public event Action? Changed;
    public SettingsService(string? path = null) => _path = path;

    public void Load()
    {
        if (_path is null || !File.Exists(_path)) return;
        try
        {
            if (new FileInfo(_path).Length > 1_048_576) throw new JsonException("Configuration too large.");
            using var doc = JsonDocument.Parse(File.ReadAllText(_path));
            int schema = doc.RootElement.TryGetProperty("schemaVersion", out var v) ? v.GetInt32() : 0;
            if (schema > 1)
            {
                _readOnly = true; Status = "Konfigurasi berasal dari versi lebih baru; file dipertahankan, memakai default sementara."; return;
            }
            Current = Validate(Migrate(JsonSerializer.Deserialize<AppSettings>(doc.RootElement.GetRawText(), JsonOptions) ?? throw new JsonException(), schema));
            Status = "Pengaturan dimuat.";
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException or FormatException)
        {
            try { File.Copy(_path, _path + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".bak", false); }
            catch (Exception backup) when (backup is IOException or UnauthorizedAccessException) { _readOnly = true; }
            Current = new(); Status = "Konfigurasi tidak valid; memakai default. File asli dipertahankan atau dicadangkan.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _readOnly = true; Status = "Konfigurasi tidak dapat dibaca; memakai default sementara."; }
    }

    public static AppSettings Migrate(AppSettings settings, int schema)
    {
        if (schema is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(schema));
        return settings with { SchemaVersion = 1 }; // v0 used the same sections without a schema marker.
    }
    public static AppSettings Validate(AppSettings value)
    {
        if (value.General is null || value.Behavior is null || value.Chat is null) throw new ArgumentException("Missing settings section.");
        var b = value.Behavior; var c = value.Chat;
        if (!Enum.IsDefined(b.Activity) || !Enum.IsDefined(b.Speed) || !Enum.IsDefined(b.SleepAfter) || !Enum.IsDefined(b.Nap) ||
            !Enum.IsDefined(c.Provider) || !Enum.IsDefined(c.Language) || !Enum.IsDefined(c.ResponseLength) || !Enum.IsDefined(c.Style) ||
            c.Model is null || !Regex.IsMatch(c.Model, @"\Agemini-[a-zA-Z0-9.\-]{1,80}\z")) throw new ArgumentException("Invalid preference.");
        static bool Finite(params double[] numbers) => numbers.All(double.IsFinite);
        var w = value.SettingsWindow;
        if (w is not null && (!Finite(w.Left, w.Top, w.Width, w.Height) || w.Width < 760 || w.Height < 520 || w.Width > 10000 || w.Height > 10000)) w = null;
        var m = value.Mascot;
        if (m is not null && !Finite(m.Left, m.MonitorLeft, m.MonitorTop)) m = null;
        var last = value.LastUpdateCheck;
        if (last > DateTimeOffset.UtcNow.AddMinutes(5)) last = null;
        return value with { SchemaVersion = 1, SettingsWindow = w, Mascot = m, LastUpdateCheck = last };
    }

    public bool Update(AppSettings settings)
    {
        Current = Validate(settings);
        bool saved = Save(); Changed?.Invoke(); return saved;
    }
    public void Reset() => Update(new());
    public bool Save()
    {
        if (_path is null) { Status = "Pengaturan berlaku selama sesi uji."; return true; }
        if (_readOnly) return false;
        string temporary = _path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, Current, JsonOptions); stream.Flush(true); }
            if (File.Exists(_path)) File.Replace(temporary, _path, _path + ".bak", true);
            else File.Move(temporary, _path);
            Status = "Pengaturan tersimpan otomatis."; return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { Status = "Gagal menyimpan. Perubahan tetap berlaku selama sesi; coba simpan kembali."; return false; }
    }
}

public sealed class PersistentStartupStore(SettingsService settings) : IStartupStore
{
    private readonly RegistryStartupStore _registry = new();
    public string? ReadCommand() => _registry.ReadCommand();
    public void WriteCommand(string command) => _registry.WriteCommand(command);
    public void DeleteCommand() => _registry.DeleteCommand();
    public bool ReadStartHidden() => settings.Current.General.StartHidden;
    public void WriteStartHidden(bool hidden)
    {
        if (!settings.Update(settings.Current with { General = settings.Current.General with { StartHidden = hidden } }))
            throw new IOException("Pilihan tersembunyi belum tersimpan ke disk.");
    }
}
