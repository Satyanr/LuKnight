using LuKnight.Assistant;

namespace LuKnight.Services;

public sealed class LocalSchedulerService
{
    private readonly LocalScheduleStore
        _store;

    private ScheduledSkill[]
        _schedules =
        [];


    public LocalSchedulerService(
        LocalScheduleStore? store = null)
    {
        _store =
            store ??
            new LocalScheduleStore();
    }


    public IReadOnlyList<ScheduledSkill>
        Schedules =>
        Array.AsReadOnly(
            _schedules);


    public IReadOnlyList<LocalScheduleLoadIssue>
        LoadIssues
    {
        get;
        private set;
    } = [];


    public void Load()
    {
        LocalScheduleLoadResult result =
            _store.Load();

        _schedules =
            result.Schedules
                .OrderBy(
                    item =>
                        item.DueAtUtc)
                .ThenBy(
                    item =>
                        item.Id)
                .ToArray();

        LoadIssues =
            result.Issues;
    }


    public IReadOnlyList<ScheduledSkill>
        GetDue(
            DateTimeOffset now)
    {
        DateTimeOffset utc =
            now.ToUniversalTime();

        return _schedules
            .Where(
                item =>
                    item.Enabled &&
                    item.DueAtUtc <=
                        utc)
            .OrderBy(
                item =>
                    item.DueAtUtc)
            .ThenBy(
                item =>
                    item.Id)
            .ToArray();
    }
    public bool Upsert(
        ScheduledSkill schedule,
        out string error)
    {
        if (!LocalSchedulePolicy
                .TryNormalize(
                    schedule,
                    out ScheduledSkill normalized,
                    out error))
        {
            return false;
        }


        var next =
            _schedules
                .Where(
                    item =>
                        item.Id !=
                        normalized.Id)
                .Append(
                    normalized)
                .OrderBy(
                    item =>
                        item.DueAtUtc)
                .ThenBy(
                    item =>
                        item.Id)
                .ToArray();


        if (next.Length >
            LocalSchedulePolicy.MaxSchedules)
        {
            error =
                "Jumlah jadwal melebihi batas.";

            return false;
        }


        if (!_store.Save(
                next,
                out error))
        {
            return false;
        }


        _schedules =
            next;

        return true;
    }


    public bool Remove(
        Guid id,
        out string error)
    {
        error =
            string.Empty;


        ScheduledSkill[] next =
            _schedules
                .Where(
                    item =>
                        item.Id != id)
                .ToArray();


        if (next.Length ==
            _schedules.Length)
        {
            error =
                "Jadwal tidak ditemukan.";

            return false;
        }


        if (!_store.Save(
                next,
                out error))
        {
            return false;
        }


        _schedules =
            next;

        return true;
    }
    public IReadOnlyList<ScheduledSkill>
        GetUnpresentedDue(
            DateTimeOffset now)
    {
        DateTimeOffset utc =
            now.ToUniversalTime();

        return _schedules
            .Where(
                item =>
                    item.Enabled &&
                    item.LastPresentedAtUtc is null &&
                    item.DueAtUtc <= utc)
            .OrderBy(
                item =>
                    item.DueAtUtc)
            .ThenBy(
                item =>
                    item.Id)
            .ToArray();
    }

    public bool MarkPresented(
        Guid id,
        DateTimeOffset presentedAt,
        out string error)
    {
        error =
            string.Empty;

        DateTimeOffset utc =
            presentedAt.ToUniversalTime();

        int index =
            Array.FindIndex(
                _schedules,
                item =>
                    item.Id == id);

        if (index < 0)
        {
            error =
                "Jadwal tidak ditemukan.";

            return false;
        }


        ScheduledSkill current =
            _schedules[index];


        // Idempotent.
        if (current.LastPresentedAtUtc is not null)
        {
            return true;
        }


        if (!current.Enabled)
        {
            error =
                "Jadwal tidak aktif.";

            return false;
        }


        if (current.DueAtUtc >
            utc)
        {
            error =
                "Jadwal belum jatuh tempo.";

            return false;
        }


        ScheduledSkill updated =
            current with
            {
                LastPresentedAtUtc =
                    utc
            };


        ScheduledSkill[] next =
            _schedules.ToArray();

        next[index] =
            updated;


        if (!_store.Save(
                next,
                out error))
        {
            return false;
        }


        _schedules =
            next
                .OrderBy(
                    item =>
                        item.DueAtUtc)
                .ThenBy(
                    item =>
                        item.Id)
                .ToArray();

        return true;
    }
}
