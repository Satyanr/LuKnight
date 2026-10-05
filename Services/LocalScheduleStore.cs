using System.IO;
using System.Text.Json;
using LuKnight.Assistant;

namespace LuKnight.Services;

public sealed record LocalScheduleFile
{
    public int SchemaVersion
    {
        get;
        init;
    } = 2;

    public ScheduledSkill[]
        Schedules
    {
        get;
        init;
    } = [];
}


public sealed record LocalScheduleLoadIssue(
    string Message);


public sealed record LocalScheduleLoadResult(
    IReadOnlyList<ScheduledSkill> Schedules,
    IReadOnlyList<LocalScheduleLoadIssue> Issues);

public sealed class LocalScheduleStore
{
    private const long MaxFileBytes =
        256 * 1024;

    private readonly string
        _path;

    private bool
        _readOnly;


    public bool IsReadOnly =>
        _readOnly;


    public LocalScheduleStore(
        string? path = null)
    {
        _path =
            path ??
            System.IO.Path.Combine(
                SettingsService.UserDirectory,
                "schedules.json");
    }


    public string Path =>
        _path;


    public LocalScheduleLoadResult Load()
    {
        _readOnly =
            false;


        CommittedStateRecovery
            .DeleteUncommittedTemporary(
                _path);


        CommittedStateCandidateStatus
            primary =
                TryReadCandidate(
                    _path,
                    out LocalScheduleLoadResult
                        primaryResult);


        if (primary ==
            CommittedStateCandidateStatus.Valid)
        {
            return
                primaryResult;
        }


        if (primary ==
            CommittedStateCandidateStatus
                .FutureVersion)
        {
            _readOnly =
                true;

            return
                primaryResult;
        }


        string backupPath =
            CommittedStateRecovery
                .BackupPath(
                    _path);


        CommittedStateCandidateStatus
            backup =
                TryReadCandidate(
                    backupPath,
                    out LocalScheduleLoadResult
                        backupResult);


        if (backup ==
            CommittedStateCandidateStatus.Valid)
        {
            if (!CommittedStateRecovery
                    .TryRestoreValidatedBackup(
                        _path))
            {
                _readOnly =
                    true;
            }


            return
                backupResult;
        }


        if (backup ==
            CommittedStateCandidateStatus
                .FutureVersion)
        {
            _readOnly =
                true;

            return
                backupResult;
        }


        if (primary ==
            CommittedStateCandidateStatus.Invalid)
        {
            if (!CommittedStateRecovery
                    .TryPreserveInvalidPrimary(
                        _path))
            {
                _readOnly =
                    true;
            }
        }
        else if (
            primary ==
            CommittedStateCandidateStatus
                .Inaccessible)
        {
            _readOnly =
                true;
        }


        return
            primaryResult;
    }

    private CommittedStateCandidateStatus TryReadCandidate(string path, out LocalScheduleLoadResult result)
    {
        result = new([], []);
        if (!File.Exists(path)) return CommittedStateCandidateStatus.Missing;
        try
        {
            if (new FileInfo(path).Length is <= 0 or > MaxFileBytes)
            {
                result = new([], [new("File jadwal kosong atau terlalu besar.")]);
                return CommittedStateCandidateStatus.Invalid;
            }
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
            int schema = 2;
            foreach (JsonProperty property in json.RootElement.EnumerateObject())
                if (property.Name.Equals("schemaVersion", StringComparison.OrdinalIgnoreCase))
                    schema = property.Value.GetInt32();
            if (schema > 2)
            {
                result = new([], [new("Schema jadwal berasal dari versi aplikasi lebih baru.")]);
                return CommittedStateCandidateStatus.FutureVersion;
            }
            if (schema < 1)
            {
                result = new([], [new("Schema jadwal tidak didukung.")]);
                return CommittedStateCandidateStatus.Invalid;
            }
            LocalScheduleFile file = JsonSerializer.Deserialize<LocalScheduleFile>(json.RootElement.GetRawText(),
                SettingsService.JsonOptions) ?? throw new JsonException();
            if (file.Schedules is null)
            {
                result = new([], [new("Daftar jadwal tidak valid.")]);
                return CommittedStateCandidateStatus.Invalid;
            }
            if (file.Schedules.Length > LocalSchedulePolicy.MaxSchedules)
            {
                result = new([], [new("Jumlah jadwal melebihi batas.")]);
                return CommittedStateCandidateStatus.Invalid;
            }
            var issues = new List<LocalScheduleLoadIssue>();
            var schedules =
                new List<ScheduledSkill>();

            var ids =
                new HashSet<Guid>();


            foreach (ScheduledSkill item
                     in file.Schedules)
            {
                if (item is null)
                {
                    issues.Add(new("Entri jadwal tidak valid."));
                    continue;
                }

                if (!LocalSchedulePolicy
                        .TryNormalize(
                            item,
                            out ScheduledSkill normalized,
                            out string error))
                {
                    issues.Add(
                        new(
                            error));

                    continue;
                }


                if (!ids.Add(
                        normalized.Id))
                {
                    issues.Add(
                        new(
                            $"ID jadwal {normalized.Id} duplikat."));

                    continue;
                }


                schedules.Add(
                    normalized);
            }


            result = new(schedules.AsReadOnly(), issues.AsReadOnly());
            return CommittedStateCandidateStatus.Valid;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException or FormatException)
        {
            result = new([], [new("Jadwal tidak dapat dimuat karena file tidak valid atau tidak dapat diakses.")]);
            return CommittedStateCandidateStatus.Invalid;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result = new([], [new("Jadwal tidak dapat dimuat karena file tidak valid atau tidak dapat diakses.")]);
            return CommittedStateCandidateStatus.Inaccessible;
        }
    }

    public bool Save(
        IReadOnlyCollection<ScheduledSkill> schedules,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(
            schedules);

        error =
            string.Empty;

        if (_readOnly)
        {
            error =
                "File jadwal dipertahankan karena penyimpanan sedang read-only.";

            return false;
        }


        if (schedules.Count >
            LocalSchedulePolicy.MaxSchedules)
        {
            error =
                "Jumlah jadwal melebihi batas.";

            return false;
        }


        var normalized =
            new List<ScheduledSkill>();

        var ids =
            new HashSet<Guid>();


        foreach (ScheduledSkill item
                 in schedules)
        {
            if (item is null)
            {
                error = "Entri jadwal tidak valid.";
                return false;
            }

            if (!LocalSchedulePolicy
                    .TryNormalize(
                        item,
                        out ScheduledSkill valid,
                        out error))
            {
                return false;
            }


            if (!ids.Add(
                    valid.Id))
            {
                error =
                    $"ID jadwal {valid.Id} duplikat.";

                return false;
            }


            normalized.Add(
                valid);
        }


        string temporary =
            _path +
            ".tmp";

        string backup =
            _path +
            ".bak";


        try
        {
            string fullPath =
                System.IO.Path.GetFullPath(
                    _path);

            Directory.CreateDirectory(
                System.IO.Path.GetDirectoryName(
                    fullPath)!);


            var document =
                new LocalScheduleFile
                {
                    Schedules =
                        normalized
                            .OrderBy(
                                item =>
                                    item.DueAtUtc)
                            .ThenBy(
                                item =>
                                    item.Id)
                            .ToArray()
                };


            using (var stream =
                   new FileStream(
                       temporary,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None))
            {
                JsonSerializer.Serialize(
                    stream,
                    document,
                    SettingsService.JsonOptions);

                if (stream.Length > MaxFileBytes)
                    throw new IOException("File jadwal terlalu besar.");

                stream.Flush(
                    true);
            }


            if (File.Exists(
                    _path))
            {
                File.Replace(
                    temporary,
                    _path,
                    backup,
                    true);
            }
            else
            {
                File.Move(
                    temporary,
                    _path);
            }


            return true;
        }
        catch (Exception ex)
            when (ex is
                IOException or
                UnauthorizedAccessException)
        {
            error =
                "Jadwal tidak dapat disimpan ke penyimpanan lokal.";

            CommittedStateRecovery
                .DeleteUncommittedTemporary(
                    _path);

            return false;
        }
    }
}
