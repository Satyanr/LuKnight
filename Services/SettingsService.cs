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
        if (_path is null)
            return;


        _readOnly =
            false;


        CommittedStateRecovery
            .DeleteUncommittedTemporary(
                _path);


        CommittedStateCandidateStatus
            primary =
                TryReadCandidate(
                    _path,
                    out AppSettings primarySettings);


        if (primary ==
            CommittedStateCandidateStatus.Valid)
        {
            Current =
                primarySettings;

            Status =
                "Pengaturan dimuat.";

            return;
        }


        //
        // Never downgrade a file produced by
        // a newer application version.
        //

        if (primary ==
            CommittedStateCandidateStatus
                .FutureVersion)
        {
            _readOnly =
                true;

            Current =
                new AppSettings();

            Status =
                "Konfigurasi berasal dari versi lebih baru; " +
                "file dipertahankan, memakai default sementara.";

            return;
        }


        string backupPath =
            CommittedStateRecovery
                .BackupPath(
                    _path);


        CommittedStateCandidateStatus
            backup =
                TryReadCandidate(
                    backupPath,
                    out AppSettings backupSettings);


        if (backup ==
            CommittedStateCandidateStatus.Valid)
        {
            Current =
                backupSettings;


            bool healed =
                CommittedStateRecovery
                    .TryRestoreValidatedBackup(
                        _path);


            if (healed)
            {
                Status =
                    "Pengaturan dipulihkan dari backup terakhir.";
            }
            else
            {
                //
                // Backup can be used in memory,
                // but do not risk overwriting the
                // only known-good copy.
                //

                _readOnly =
                    true;

                Status =
                    "Pengaturan dipulihkan sementara dari backup; " +
                    "penyimpanan tetap read-only.";
            }


            return;
        }


        if (backup ==
            CommittedStateCandidateStatus
                .FutureVersion)
        {
            _readOnly =
                true;

            Current =
                new AppSettings();

            Status =
                "Backup konfigurasi berasal dari versi aplikasi lebih baru.";

            return;
        }


        Current =
            new AppSettings();


        if (primary ==
            CommittedStateCandidateStatus.Invalid)
        {
            bool preserved =
                CommittedStateRecovery
                    .TryPreserveInvalidPrimary(
                        _path);


            if (!preserved)
            {
                _readOnly =
                    true;
            }


            Status =
                "Konfigurasi tidak valid dan backup valid tidak tersedia; " +
                "memakai default.";
        }
        else if (
            primary ==
            CommittedStateCandidateStatus
                .Inaccessible)
        {
            _readOnly =
                true;

            Status =
                "Konfigurasi tidak dapat dibaca; memakai default sementara.";
        }
    }

    private static
        CommittedStateCandidateStatus
        TryReadCandidate(
            string path,
            out AppSettings settings)
    {
        settings =
            new AppSettings();


        if (!File.Exists(
                path))
        {
            return
                CommittedStateCandidateStatus
                    .Missing;
        }


        try
        {
            if (new FileInfo(
                    path)
                .Length >
                1_048_576)
            {
                return
                    CommittedStateCandidateStatus
                        .Invalid;
            }


            using JsonDocument doc =
                JsonDocument.Parse(
                    File.ReadAllText(
                        path));


            int schema = 0;
            foreach (JsonProperty property in doc.RootElement.EnumerateObject())
                if (property.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase))
                    schema = property.Value.GetInt32();

            if (schema > 2)
            {
                return
                    CommittedStateCandidateStatus
                        .FutureVersion;
            }


            AppSettings parsed =
                JsonSerializer
                    .Deserialize<AppSettings>(
                        doc.RootElement
                            .GetRawText(),
                        JsonOptions)
                ?? throw new JsonException();


            settings =
                Validate(
                    Migrate(
                        parsed,
                        schema));


            return
                CommittedStateCandidateStatus
                    .Valid;
        }
        catch (Exception ex)
            when (ex is
                JsonException or
                ArgumentException or
                InvalidOperationException or
                OverflowException or
                FormatException)
        {
            return
                CommittedStateCandidateStatus
                    .Invalid;
        }
        catch (Exception ex)
            when (ex is
                IOException or
                UnauthorizedAccessException)
        {
            return
                CommittedStateCandidateStatus
                    .Inaccessible;
        }
    }

    public static AppSettings Migrate(
        AppSettings settings,
        int schema)
    {
        if (schema is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schema));
        }

        return settings with
        {
            SchemaVersion =
                2,

            Companion =
                settings.Companion ??
                new CompanionSettings()
        };
    }
    public static AppSettings Validate(AppSettings value)
    {
        if (value.General is null || value.Behavior is null || value.Chat is null || value.Companion is null) throw new ArgumentException("Missing settings section.");
        var b = value.Behavior; var c = value.Chat;
        if (!Enum.IsDefined(b.Activity) || !Enum.IsDefined(b.Speed) || !Enum.IsDefined(b.SleepAfter) || !Enum.IsDefined(b.Nap) ||
            !Enum.IsDefined(c.DesktopPermission) || !Enum.IsDefined(c.Provider) || !Enum.IsDefined(c.Language) || !Enum.IsDefined(c.ResponseLength) || !Enum.IsDefined(c.Style) ||
            !Enum.IsDefined(c.VoiceLanguage) || !Enum.IsDefined(c.VoiceModel) || !Enum.IsDefined(c.VoiceSubmissionMode) || !Enum.IsDefined(c.TextToSpeechMode) ||
            c.TextToSpeechVoice is null || c.TextToSpeechVoice.Length > 200 ||
            c.TextToSpeechRate is < -10 or > 10 || c.TextToSpeechVolume is < 0 or > 100 ||
            c.Model is null || !Regex.IsMatch(c.Model, @"\Agemini-[a-zA-Z0-9.\-]{1,80}\z")) throw new ArgumentException("Invalid preference.");
        static bool Finite(params double[] numbers) => numbers.All(double.IsFinite);
        var w = value.SettingsWindow;
        if (w is not null && (!Finite(w.Left, w.Top, w.Width, w.Height) || w.Width < 760 || w.Height < 520 || w.Width > 10000 || w.Height > 10000)) w = null;
        var m = value.Mascot;
        if (m is not null && !Finite(m.Left, m.MonitorLeft, m.MonitorTop)) m = null;
        var last = value.LastUpdateCheck;
        if (last > DateTimeOffset.UtcNow.AddMinutes(5)) last = null;
        return value with { SchemaVersion = 2, SettingsWindow = w, Mascot = m, LastUpdateCheck = last };
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
        { CommittedStateRecovery.DeleteUncommittedTemporary(_path); Status = "Gagal menyimpan. Perubahan tetap berlaku selama sesi; coba simpan kembali."; return false; }
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
