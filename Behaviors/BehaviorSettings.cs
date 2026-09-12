namespace LuKnight.Behaviors;

public enum ActivityLevel { Calm, Balanced, Active }
public enum MovementSpeed { Slow, Normal, Fast }
public enum SleepDelay { OneMinute, TwoMinutes, FiveMinutes, Never }
public enum NapDuration { Short, Normal, Long }

// Product preferences are separate from transient states and physical constants.
public sealed record BehaviorOptions
{
    public bool Enabled { get; init; } = true;
    public ActivityLevel Activity { get; init; } = ActivityLevel.Balanced;
    public MovementSpeed Speed { get; init; } = MovementSpeed.Normal;
    public bool AllowSleep { get; init; } = true;
    public SleepDelay SleepAfter { get; init; } = SleepDelay.TwoMinutes;
    public NapDuration Nap { get; init; } = NapDuration.Normal;
    public bool ExploreWindows { get; init; } = true;
    public bool JumpBetweenWindows { get; init; } = true;
    public bool HangingClimbing { get; init; } = true;
    public bool LookAtCursor { get; init; } = true;
    public bool ReactToCursor { get; init; } = true;
    public bool WakeAtCursor { get; init; } = true;
    public double SpeedFactor => Speed switch { MovementSpeed.Slow => .65, MovementSpeed.Fast => 1.45, _ => 1 };
    public double ActivityDelay => Activity switch { ActivityLevel.Calm => 1.8, ActivityLevel.Active => .65, _ => 1 };
    public double AdventureChance => Activity switch { ActivityLevel.Calm => .35, ActivityLevel.Active => 1, _ => .7 };
    public TimeSpan SleepThreshold => SleepAfter switch
    {
        SleepDelay.OneMinute => TimeSpan.FromMinutes(1), SleepDelay.FiveMinutes => TimeSpan.FromMinutes(5),
        SleepDelay.Never => TimeSpan.MaxValue, _ => TimeSpan.FromMinutes(2)
    };
    public double NapFactor => Nap switch { NapDuration.Short => .5, NapDuration.Long => 2, _ => 1 };
    public bool CanSleep => Enabled && AllowSleep && SleepAfter != SleepDelay.Never;
    public bool CanExplore => Enabled && ExploreWindows;
}

// Session lifetime; durable configuration is phase 6F.
public sealed class BehaviorSettings
{
    public BehaviorOptions Current { get; private set; } = new();
    public event Action<BehaviorOptions>? Changed;
    public void Apply(BehaviorOptions options)
    {
        if (!Enum.IsDefined(options.Activity) || !Enum.IsDefined(options.Speed) ||
            !Enum.IsDefined(options.SleepAfter) || !Enum.IsDefined(options.Nap))
            throw new ArgumentOutOfRangeException(nameof(options));
        if (Current == options) return;
        Current = options;
        Changed?.Invoke(options);
    }
    public void Reset() => Apply(new());
}
