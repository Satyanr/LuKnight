using System.IO;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private static void
        CheckNotificationCoordinator()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                "LuKnight-notification-check-" +
                Guid.NewGuid()
                    .ToString("N"));

        Directory.CreateDirectory(
            directory);


        try
        {
            DateTimeOffset now =
                new(
                    2026,
                    10,
                    5,
                    10,
                    0,
                    0,
                    TimeSpan.Zero);


            var scheduler =
                new LocalSchedulerService(
                    new LocalScheduleStore(
                        Path.Combine(
                            directory,
                            "schedules.json")));

            scheduler.Load();


            var reminder =
                new ScheduledSkill
                {
                    Id =
                        Guid.NewGuid(),

                    Enabled =
                        true,

                    DisplayName =
                        "Coordinator reminder",

                    CreatedAtUtc =
                        now.AddHours(
                            -1),

                    DueAtUtc =
                        now.AddMinutes(
                            -10),

                    Invocation =
                        new ScheduledSkillInvocation
                        {
                            SkillId =
                                "search-downloads",

                            Argument =
                                "invoice"
                        }
                };


            Require(
                scheduler.Upsert(
                    reminder,
                    out _),
                "Coordinator reminder fixture could not be saved.");


            CompanionSettings preferences =
                new()
                {
                    Enabled =
                        true
                };


            bool applicationContext =
                true;

            bool assistantBusy =
                false;


            AssistantRuntimeContext context =
                CreateCompanionContext(
                    "CodeEditor");


            var capabilities =
                new AssistantCapabilityRegistry(
                    [
                        new(
                            "feature:chat",
                            AssistantCapabilityKind.Feature,
                            "Chat",
                            "Chat assistance",
                            [])
                    ]);


            var coordinator =
                new AssistantNotificationCoordinator(
                    scheduler,
                    new LocalCompanionAdvisor(),
                    capabilities,
                    () =>
                        preferences,
                    () =>
                        applicationContext,
                    () =>
                        assistantBusy,
                    () =>
                        context);


            int reminderEvents =
                0;

            int companionEvents =
                0;

            int cancellationEvents =
                0;


            CompanionSuggestionNotification?
                companionNotification =
                    null;


            coordinator.ReminderDue +=
                notification =>
                {
                    reminderEvents++;

                    Require(
                        notification.ScheduleId ==
                            reminder.Id,
                        "Coordinator surfaced wrong reminder.");


                    Require(
                        coordinator
                            .MarkReminderPresented(
                                notification.ScheduleId,
                                notification.ObservedAtUtc,
                                out _),
                        "Coordinator could not commit reminder presentation.");
                };


            coordinator.CompanionSuggestionReady +=
                notification =>
                {
                    companionEvents++;

                    companionNotification =
                        notification;
                };


            coordinator
                .CompanionNotificationCancellationRequested +=
                    () =>
                        cancellationEvents++;


            //
            // Reminder surfaces immediately.
            // Same poll seeds companion dwell after
            // synchronous reminder presentation.
            //

            coordinator.Poll(
                now);


            Require(
                reminderEvents ==
                    1 &&
                companionEvents ==
                    0,
                "Coordinator priority/dwell behavior changed.");


            //
            // Stable app context reaches suggestion.
            //

            coordinator.Poll(
                now +
                CompanionSuggestionGate
                    .StabilityDelay);


            Require(
                companionEvents ==
                    1 &&
                companionNotification is not null,
                "Coordinator did not surface stable companion candidate.");


            Require(
                coordinator
                    .MarkCompanionPresented(
                        companionNotification!.Key,
                        companionNotification.ObservedAtUtc),
                "Coordinator could not commit companion presentation.");


            Require(
                coordinator
                    .TryConsumeCompanion(
                        companionNotification.Key,
                        out CompanionSuggestionCandidate
                            consumed) &&
                consumed.Key ==
                    companionNotification.Key,
                "Coordinator could not consume valid companion candidate.");


            Require(
                !coordinator.TryConsumeCompanion(
                    companionNotification.Key,
                    out _),
                "Companion candidate was consumed twice.");


            //
            // Reminder recovery must not re-notify
            // during same process session.
            //

            coordinator.Poll(
                now.AddMinutes(
                    6));


            Require(
                reminderEvents ==
                    1,
                "Reminder was presented twice in one process session.");


            //
            // Remove reminder as a blocker.
            //

            Require(
                scheduler.Acknowledge(
                    reminder.Id,
                    ScheduledReminderDisposition
                        .Dismissed,
                    now.AddMinutes(
                        7),
                    out _),
                "Coordinator fixture reminder could not be acknowledged.");


            //
            // Same key is eligible again after cooldown.
            //

            coordinator.Poll(now.AddMinutes(7));

            DateTimeOffset later =
                now + CompanionSuggestionGate.StabilityDelay +
                CompanionSuggestionGate
                    .SameKeyCooldown;


            coordinator.Poll(
                later);


            Require(
                companionEvents ==
                    2 &&
                companionNotification is not null,
                "Companion suggestion did not return after cooldown.");


            Require(
                coordinator.MarkCompanionPresented(
                    companionNotification!.Key,
                    companionNotification.ObservedAtUtc),
                "Second companion presentation could not be committed.");


            //
            // Live preference changes invalidate
            // an already-presented tray candidate.
            //

            preferences =
                preferences with
                {
                    Coding =
                        false
                };


            Require(
                !coordinator.TryConsumeCompanion(
                    companionNotification.Key,
                    out _),
                "Stale category preference did not invalidate companion candidate.");


            //
            // Turning proactive mode off clears
            // pending state and requests tray cancel.
            //

            preferences =
                preferences with
                {
                    Enabled =
                        false
                };


            coordinator.Poll(
                later.AddMinutes(
                    1));


            Require(
                cancellationEvents >
                    0,
                "Disabled proactive preference did not request UI cancellation.");


            //
            // Busy assistant never produces
            // proactive suggestion.
            //

            preferences =
                new CompanionSettings
                {
                    Enabled =
                        true
                };

            assistantBusy =
                true;


            var busyCoordinator =
                new AssistantNotificationCoordinator(
                    scheduler,
                    new LocalCompanionAdvisor(),
                    capabilities,
                    () =>
                        preferences,
                    () =>
                        true,
                    () =>
                        assistantBusy,
                    () =>
                        context);


            int busySuggestions =
                0;

            busyCoordinator
                .CompanionSuggestionReady +=
                    _ =>
                        busySuggestions++;


            busyCoordinator.Poll(
                now);

            busyCoordinator.Poll(
                now +
                CompanionSuggestionGate
                    .StabilityDelay +
                TimeSpan.FromMinutes(
                    1));


            Require(
                busySuggestions ==
                    0,
                "Busy assistant produced proactive suggestion.");
        }
        finally
        {
            try
            {
                Directory.Delete(
                    directory,
                    recursive:
                        true);
            }
            catch
            {
                // Test cleanup only.
            }
        }
    }
}
