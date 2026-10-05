namespace LuKnight.Assistant;

public enum ScheduledReminderDisposition
{
    RunRequested,
    Dismissed
}

public sealed record ScheduledSkillInvocation
{
    public string SkillId
    {
        get;
        init;
    } = string.Empty;

    public string Argument
    {
        get;
        init;
    } = string.Empty;

    public IReadOnlyDictionary<string, string>?
        Parameters
    {
        get;
        init;
    }
}


public sealed record ScheduledSkill
{
    public Guid Id
    {
        get;
        init;
    }

    public bool Enabled
    {
        get;
        init;
    } = true;

    public string DisplayName
    {
        get;
        init;
    } = string.Empty;

    public DateTimeOffset DueAtUtc
    {
        get;
        init;
    }

    public ScheduledSkillInvocation Invocation
    {
        get;
        init;
    } = new();

    public DateTimeOffset CreatedAtUtc
    {
        get;
        init;
    }

    public DateTimeOffset? LastPresentedAtUtc
    {
        get;
        init;
    }
    public DateTimeOffset?
        AcknowledgedAtUtc
    {
        get;
        init;
    }

    public ScheduledReminderDisposition?
        Disposition
    {
        get;
        init;
    }
}
