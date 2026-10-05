using LuKnight.Assistant;

namespace LuKnight.Services;

public sealed class LocalSchedulerService
{
    public static readonly TimeSpan
        ReminderRecoveryDelay =
            TimeSpan.FromMinutes(
                5);
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
        GetReminderCandidates(
            DateTimeOffset now)
    {
        DateTimeOffset utc =
            now.ToUniversalTime();

        DateTimeOffset reofferBefore =
            utc -
            ReminderRecoveryDelay;


        return _schedules
            .Where(
                item =>
                    item.Enabled &&
                    item.DueAtUtc <= utc &&
                    item.AcknowledgedAtUtc is null &&
                    (
                        item.LastPresentedAtUtc is null ||
                        item.LastPresentedAtUtc <=
                            reofferBefore
                    ))
            .OrderBy(
                item =>
                    item.DueAtUtc)
            .ThenBy(
                item =>
                    item.Id)
            .ToArray();
    }

    public IReadOnlyList<ScheduledSkill>
        GetUnpresentedDue(
            DateTimeOffset now) =>
        GetReminderCandidates(
            now);

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


        if (current.AcknowledgedAtUtc is not null)
        {
            error =
                "Reminder sudah ditanggapi.";

            return false;
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

        if (current.LastPresentedAtUtc is
                DateTimeOffset lastPresented &&
            utc <
                lastPresented +
                ReminderRecoveryDelay)
        {
            return true;
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
    public bool TryGetDue(
        Guid id,
        DateTimeOffset now,
        out ScheduledSkill schedule)
    {
        DateTimeOffset utc =
            now.ToUniversalTime();

        ScheduledSkill? found =
            _schedules
                .FirstOrDefault(
                    item =>
                        item.Id == id &&
                        item.Enabled &&
                        item.AcknowledgedAtUtc is null &&
                        item.DueAtUtc <=
                            utc);

        if (found is null)
        {
            schedule =
                default!;

            return false;
        }

        schedule =
            found;

        return true;
    }
    public bool Acknowledge(
        Guid id,
        ScheduledReminderDisposition disposition,
        DateTimeOffset acknowledgedAt,
        out string error)
    {
        error =
            string.Empty;


        if (!Enum.IsDefined(
                disposition))
        {
            error =
                "Disposition reminder tidak valid.";

            return false;
        }


        DateTimeOffset utc =
            acknowledgedAt
                .ToUniversalTime();


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


        if (current.AcknowledgedAtUtc is not null)
        {
            if (current.Disposition ==
                disposition)
            {
                // Idempotent.
                return true;
            }

            error =
                "Reminder sudah ditanggapi dengan aksi lain.";

            return false;
        }


        ScheduledSkill updated =
            current with
            {
                AcknowledgedAtUtc =
                    utc,

                Disposition =
                    disposition
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
