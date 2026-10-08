using System.IO;
using System.Windows;
using System.Windows.Threading;
using LuKnight.Behaviors;
using LuKnight.Physics;
using LuKnight.Views;

internal static partial class Program
{
    private static void CheckIdlePerformanceBoundaries()
    {
        using var physics = new CharacterPhysicsController(new Window(), new CharacterView(),
            cursorSource: () => new Point(10, 10), pointerClock: () => 1);
        bool Subscribed() => Get<bool>(physics, "_renderingSubscribed");
        Require(!Subscribed(), "Idle physics subscribed to rendering.");
        physics.SetSuspended(true);
        physics.SetSuspended(false);
        Require(!Subscribed(), "Resuming idle physics activated rendering.");
        physics.PrepareGrab(new Point(10, 10), 1);
        Require(Subscribed(), "Prepared grab did not activate sampling.");
        physics.PrepareGrab(new Point(10, 10), 1);
        physics.CancelPreparedGrab();
        Require(!Subscribed(), "Cancelled grab retained rendering.");
        physics.StartFall(0, 0);
        Require(Subscribed(), "Active fall did not activate rendering.");
        physics.SetSuspended(true);
        physics.SetSuspended(true);
        Require(!Subscribed(), "Suspended physics retained rendering.");
        typeof(CharacterPhysicsController).GetField("_lastTickAt", Private)!
            .SetValue(physics, DateTime.UtcNow.AddHours(-1));
        physics.SetSuspended(false);
        Require(Subscribed(), "Resumed fall did not restore rendering.");
        Require(DateTime.UtcNow - Get<DateTime>(physics, "_lastTickAt") < TimeSpan.FromSeconds(1),
            "Physics resume retained hidden duration in timing baseline.");
        physics.ResetMotion();
        Require(!Subscribed(), "Reset physics retained rendering.");
        physics.StartFall(0, 0);
        physics.SetSuspended(true);
        physics.ResetMotion();
        Require(!Subscribed() && !physics.IsActive, "Reset suspended physics activated rendering.");
        physics.SetSuspended(false);
        Require(!Subscribed(), "Resume after reset activated idle rendering.");
        bool landed = false;
        physics.Landed += (_, _, _) =>
        {
            landed = true;
            Require(!Subscribed(), "Settlement did not release rendering before Landed.");
            physics.StartFall(0, -50);
        };
        physics.StartFall(0, 0);
        typeof(CharacterPhysicsController).GetMethod("Settle", Private)!
            .Invoke(physics, new object?[] { 0.0, null });
        Require(landed && physics.IsFalling && Subscribed(), "Landed could not restart physics.");
        physics.Dispose();
        physics.Dispose();
        Require(!Subscribed() && !physics.IsActive, "Disposed physics retained active rendering.");
        physics.SetSuspended(true);
        physics.SetSuspended(false);
        physics.ResetMotion();
        Require(!Subscribed(), "Disposed physics resubscribed after reset/resume.");
        bool rejected = false;
        try { physics.PrepareGrab(new Point(10, 10), 1); }
        catch (ObjectDisposedException) { rejected = true; }
        Require(rejected, "Disposed physics accepted PrepareGrab.");
    }

    private static void CheckBehaviorIdleTimers()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "LuKnight.csproj")))
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Repository not found.");
        string source = File.ReadAllText(Path.Combine(root, "Behaviors", "BehaviorController.cs"));
        Require(source.Contains("StopLoopTimers();", StringComparison.Ordinal) &&
                source.Contains("StartLoopTimers();", StringComparison.Ordinal) &&
                source.Contains("BehaviorPauseReason.Hidden", StringComparison.Ordinal),
            "Behavior does not implement hidden timer suspension/resumption.");
        using var behavior = new BehaviorController(new Window(), new CharacterView());
        var timer = Get<DispatcherTimer>(behavior, "_timer");
        var movement = Get<DispatcherTimer>(behavior, "_movementTimer");
        bool Running() => timer.IsEnabled && movement.IsEnabled;
        bool Stopped() => !timer.IsEnabled && !movement.IsEnabled;
        behavior.Pause(BehaviorPauseReason.Hidden);
        behavior.Resume(BehaviorPauseReason.Hidden);
        Require(Stopped(), "Resume started behavior before Start.");
        // Seed lifecycle state without placing a hidden WPF window on a native monitor.
        typeof(BehaviorController).GetField("_started", Private)!.SetValue(behavior, true);
        typeof(BehaviorController).GetMethod("StartLoopTimers", Private)!.Invoke(behavior, null);
        Require(Running(), "Started behavior did not enable both timers.");
        behavior.Start();
        Require(Running(), "Repeated Start disturbed active timers.");
        behavior.Pause(BehaviorPauseReason.Chat | BehaviorPauseReason.Hidden);
        Require(Stopped(), "Hidden pause did not stop both timers.");
        typeof(BehaviorController).GetField("_lastMovementTimestamp", Private)!.SetValue(behavior, 0L);
        behavior.Resume(BehaviorPauseReason.Hidden);
        Require(behavior.IsPaused && Running(), "Chat pause prevented attention timers from resuming.");
        Require(Get<long>(behavior, "_lastMovementTimestamp") > 0,
            "Resume failed to reset movement timing baseline.");
        behavior.Pause(BehaviorPauseReason.Hidden);
        behavior.Resume(BehaviorPauseReason.Chat);
        Require(Stopped(), "Clearing another pause restarted hidden behavior.");
        behavior.Resume(BehaviorPauseReason.Hidden);
        Require(Running(), "Visible behavior did not resume timers.");
        behavior.Dispose();
        behavior.Dispose();
        behavior.Pause(BehaviorPauseReason.Hidden);
        behavior.Resume(BehaviorPauseReason.Hidden);
        Require(Stopped(), "Disposed behavior restarted timers.");
        bool rejected = false;
        try { behavior.Start(); }
        catch (ObjectDisposedException) { rejected = true; }
        Require(rejected, "Disposed behavior accepted Start.");
    }
}
