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
}
