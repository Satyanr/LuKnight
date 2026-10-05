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
        if (!File.Exists(
                _path))
        {
            return new(
                [],
                []);
        }


        var issues =
            new List<LocalScheduleLoadIssue>();


        try
        {
            var info =
                new FileInfo(
                    _path);

            if (info.Length is <= 0 or >
                MaxFileBytes)
            {
                return new(
                    [],
                    [
                        new(
                            "File jadwal kosong atau terlalu besar.")
                    ]);
            }


            string json =
                File.ReadAllText(
                    _path);


            LocalScheduleFile file =
                JsonSerializer.Deserialize<
                    LocalScheduleFile>(
                    json,
                    SettingsService.JsonOptions) ??
                throw new JsonException(
                    "Schedule document kosong.");


            if (file.SchemaVersion is not 1 and not 2)
            {
                return new(
                    [],
                    [
                        new(
                            "Schema jadwal tidak didukung.")
                    ]);
            }


            if (file.Schedules is null)
            {
                return new(
                    [],
                    [
                        new(
                            "Daftar jadwal tidak valid.")
                    ]);
            }


            if (file.Schedules.Length >
                LocalSchedulePolicy
                    .MaxSchedules)
            {
                return new(
                    [],
                    [
                        new(
                            "Jumlah jadwal melebihi batas.")
                    ]);
            }


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


            return new(
                schedules.AsReadOnly(),
                issues.AsReadOnly());
        }
        catch (Exception ex)
            when (ex is
                IOException or
                UnauthorizedAccessException or
                JsonException or
                ArgumentException or
                InvalidOperationException)
        {
            return new(
                [],
                [
                    new(
                        $"Jadwal tidak dapat dimuat: {ex.Message}")
                ]);
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
                $"Jadwal tidak dapat disimpan: {ex.Message}";

            try
            {
                if (File.Exists(
                        temporary))
                {
                    File.Delete(
                        temporary);
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
