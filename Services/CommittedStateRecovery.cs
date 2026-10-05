using System.IO;

namespace LuKnight.Services;


internal enum CommittedStateCandidateStatus
{
    Missing,
    Valid,
    Invalid,
    Inaccessible,
    FutureVersion
}


internal static class CommittedStateRecovery
{
    public const int MaxInvalidSnapshots = 3;

    public static string BackupPath(
        string path) =>
        path + ".bak";


    public static string TemporaryPath(
        string path) =>
        path + ".tmp";


    private static string RecoveryPath(
        string path) =>
        path + ".recover.tmp";


    public static void
        DeleteUncommittedTemporary(
            string path)
    {
        TryDelete(
            TemporaryPath(
                path));

        TryDelete(
            RecoveryPath(
                path));
    }


    public static bool
        TryPreserveInvalidPrimary(
            string path)
    {
        try
        {
            if (!File.Exists(
                    path))
            {
                return true;
            }


            string invalid =
                path +
                ".invalid-" +
                DateTime.UtcNow
                    .ToString(
                        "yyyyMMddHHmmssfff") +
                "-" +
                Guid.NewGuid()
                    .ToString("N")[..8] +
                ".bak";


            File.Copy(
                path,
                invalid,
                overwrite:
                    false);

            // File.Copy retains the primary's old timestamp. Retention tracks
            // when a forensic snapshot was taken, not when its source was edited.
            File.SetLastWriteTimeUtc(invalid, DateTime.UtcNow);
            PruneInvalidSnapshots(path, invalid);
            return true;
        }
        catch (Exception ex)
            when (ex is
                IOException or
                UnauthorizedAccessException)
        {
            return false;
        }
    }


    //
    // IMPORTANT:
    //
    // Caller must validate backup BEFORE
    // calling this method.
    //
    public static bool
        TryRestoreValidatedBackup(
            string path)
    {
        string backup =
            BackupPath(
                path);

        string recovery =
            RecoveryPath(
                path);


        if (!File.Exists(
                backup))
        {
            return false;
        }


        try
        {
            string fullPath =
                Path.GetFullPath(
                    path);


            Directory.CreateDirectory(
                Path.GetDirectoryName(
                    fullPath)!);


            //
            // Never destroy a corrupt primary
            // before preserving it.
            //

            if (!TryPreserveInvalidPrimary(
                    path))
            {
                return false;
            }


            using (
                var source =
                    new FileStream(
                        backup,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read))
            using (
                var destination =
                    new FileStream(
                        recovery,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None))
            {
                source.CopyTo(
                    destination);

                destination.Flush(
                    flushToDisk:
                        true);
            }


            File.Move(
                recovery,
                path,
                overwrite:
                    true);


            DeleteUncommittedTemporary(
                path);


            return true;
        }
        catch (Exception ex)
            when (ex is
                IOException or
                UnauthorizedAccessException)
        {
            TryDelete(
                recovery);

            return false;
        }
    }


    private static void
        PruneInvalidSnapshots(
            string path,
            string preservedSnapshot)
    {
        try
        {
            string fullPath =
                Path.GetFullPath(
                    path);

            string directory =
                Path.GetDirectoryName(
                    fullPath)!;

            string name =
                Path.GetFileName(
                    fullPath);


            if (!Directory.Exists(
                    directory))
            {
                return;
            }


            string preserved = Path.GetFullPath(preservedSnapshot);

            string[] invalid =
                Directory
                    .GetFiles(
                        directory,
                        name +
                        ".invalid-*.bak",
                        SearchOption.TopDirectoryOnly)
                    // Protect this capture even if legacy snapshots have future
                    // timestamps or the system clock has moved backwards.
                    .OrderByDescending(file => string.Equals(file, preserved, StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(
                        file =>
                            File.GetLastWriteTimeUtc(
                                file))
                    .ThenByDescending(
                        file =>
                            file,
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();


            foreach (string stale
                     in invalid.Skip(
                         MaxInvalidSnapshots))
            {
                TryDelete(
                    stale);
            }
        }
        catch (Exception ex)
            when (ex is
                IOException or
                UnauthorizedAccessException)
        {
            // Best-effort forensic retention.
        }
    }

    private static void TryDelete(
        string path)
    {
        try
        {
            if (File.Exists(
                    path))
            {
                File.Delete(
                    path);
            }
        }
        catch (Exception ex)
            when (ex is
                IOException or
                UnauthorizedAccessException)
        {
            // Best-effort stale-file cleanup.
        }
    }
}
