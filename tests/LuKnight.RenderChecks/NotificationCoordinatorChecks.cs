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


            coordinator.Poll(now);
            Require(reminderEvents == 1 && companionEvents == 0,
                "Initial due reminder behavior changed.");

            coordinator.Poll(now + CompanionSuggestionGate.StabilityDelay);
            Require(companionEvents == 0,
                "Reminder presentation cooldown allowed proactive suggestion.");
            coordinator.Poll(now + LocalSchedulerService.ReminderRecoveryDelay - TimeSpan.FromTicks(1));
            Require(reminderEvents == 1, "Reminder was reoffered before recovery delay.");
            DateTimeOffset reminderRecovery = now + LocalSchedulerService.ReminderRecoveryDelay;
            coordinator.Poll(reminderRecovery);
            Require(reminderEvents == 2, "Unacknowledged reminder was not reoffered after recovery delay.");
            Require(companionEvents == 0, "Unacknowledged reminder allowed proactive suggestion.");

            DateTimeOffset acknowledgedAt = reminderRecovery.AddMinutes(1);
            Require(scheduler.Acknowledge(reminder.Id, ScheduledReminderDisposition.Dismissed,
                acknowledgedAt, out _), "Coordinator fixture reminder could not be acknowledged.");
            coordinator.Poll(acknowledgedAt);
            Require(companionEvents == 0, "Companion skipped fresh dwell after reminder acknowledgement.");
            DateTimeOffset companionReadyAt = acknowledgedAt + CompanionSuggestionGate.StabilityDelay;
            coordinator.Poll(companionReadyAt);
            Require(companionEvents == 1 && companionNotification is not null,
                "Companion did not resume after reminder acknowledgement.");
            Require(coordinator.MarkCompanionPresented(companionNotification!.Key, companionReadyAt),
                "Coordinator could not commit companion presentation.");

            assistantBusy = true;
            Require(!coordinator.TryConsumeCompanion(companionNotification.Key, companionReadyAt.AddSeconds(1), out _),
                "Pending proactive suggestion bypassed live assistant-busy state.");
            assistantBusy = false;
            Require(!coordinator.TryConsumeCompanion(companionNotification.Key, companionReadyAt.AddSeconds(2), out _),
                "Busy-suppressed suggestion returned when assistant became idle.");

            // A fresh dwell and cooldown are required after suppression.
            coordinator.Poll(companionReadyAt.AddSeconds(2));
            DateTimeOffset later = companionReadyAt + CompanionSuggestionGate.SameKeyCooldown;
            coordinator.Poll(later);
            Require(companionEvents == 2 && coordinator.MarkCompanionPresented(companionNotification!.Key, later),
                "Companion suggestion did not return after cooldown.");
            Require(coordinator.TryConsumeCompanion(" " + companionNotification!.Key.ToUpperInvariant() + " ",
                later, out CompanionSuggestionCandidate consumed) && consumed.Key == companionNotification.Key,
                "Coordinator could not consume normalized companion key.");
            Require(!coordinator.TryConsumeCompanion(companionNotification.Key, later, out _),
                "Companion candidate was consumed twice.");

            later += CompanionSuggestionGate.SameKeyCooldown;
            coordinator.Poll(later);
            Require(companionEvents == 3 && coordinator.MarkCompanionPresented(companionNotification.Key, later),
                "Third companion presentation could not be committed.");
            var higherPriority = reminder with
            {
                Id = Guid.NewGuid(), DisplayName = "Higher priority reminder",
                DueAtUtc = later.AddSeconds(1), LastPresentedAtUtc = null,
                AcknowledgedAtUtc = null, Disposition = null
            };
            Require(scheduler.Upsert(higherPriority, out _), "Higher-priority reminder fixture could not be saved.");
            Require(!coordinator.TryConsumeCompanion(companionNotification.Key, higherPriority.DueAtUtc, out _),
                "Pending proactive suggestion bypassed newly due reminder.");
            Require(scheduler.Acknowledge(higherPriority.Id, ScheduledReminderDisposition.Dismissed,
                higherPriority.DueAtUtc, out _), "Higher-priority reminder could not be acknowledged.");

            // Each independent invalidation starts with an actually presented balloon.
            (AssistantNotificationCoordinator Coordinator, CompanionSuggestionNotification Notification)
                PresentCompanion(DateTimeOffset at)
            {
                var fresh = new AssistantNotificationCoordinator(scheduler, new LocalCompanionAdvisor(),
                    capabilities, () => preferences, () => applicationContext, () => assistantBusy, () => context);
                CompanionSuggestionNotification? notification = null;
                fresh.CompanionSuggestionReady += value => notification = value;
                fresh.CompanionNotificationCancellationRequested += () => cancellationEvents++;
                fresh.Poll(at);
                fresh.Poll(at + CompanionSuggestionGate.StabilityDelay);
                Require(notification is not null && fresh.MarkCompanionPresented(notification.Key, notification.ObservedAtUtc),
                    "Companion invalidation fixture was not presented.");
                return (fresh, notification!);
            }

            later = higherPriority.DueAtUtc.AddMinutes(1);
            var category = PresentCompanion(later);
            preferences = preferences with { Coding = false };
            Require(!category.Coordinator.TryConsumeCompanion(category.Notification.Key,
                category.Notification.ObservedAtUtc, out _), "Stale category preference did not invalidate candidate.");
            preferences = preferences with { Coding = true };

            var disabled = PresentCompanion(later);
            preferences = preferences with { Enabled = false };
            disabled.Coordinator.Poll(disabled.Notification.ObservedAtUtc.AddSeconds(1));
            Require(cancellationEvents == 1 && !disabled.Coordinator.TryConsumeCompanion(disabled.Notification.Key,
                disabled.Notification.ObservedAtUtc.AddSeconds(1), out _), "Disabled preference did not cancel pending candidate.");
            disabled.Coordinator.Poll(disabled.Notification.ObservedAtUtc.AddSeconds(2));
            Require(cancellationEvents == 1, "Empty companion state repeatedly requested cancellation.");
            preferences = preferences with { Enabled = true };

            var busy = PresentCompanion(later);
            assistantBusy = true;
            busy.Coordinator.Poll(busy.Notification.ObservedAtUtc.AddSeconds(1));
            assistantBusy = false;
            Require(cancellationEvents == 2 && !busy.Coordinator.TryConsumeCompanion(busy.Notification.Key,
                busy.Notification.ObservedAtUtc.AddSeconds(2), out _), "Busy poll did not cancel pending candidate.");

            var lostContext = PresentCompanion(later);
            context = context with { ApplicationContextEnabled = false };
            lostContext.Coordinator.Poll(lostContext.Notification.ObservedAtUtc.AddSeconds(1));
            Require(cancellationEvents == 3 && !lostContext.Coordinator.TryConsumeCompanion(lostContext.Notification.Key,
                lostContext.Notification.ObservedAtUtc.AddSeconds(1), out _), "Lost context did not cancel stale candidate.");
            context = CreateCompanionContext("CodeEditor");

            var permission = PresentCompanion(later);
            applicationContext = false;
            Require(!permission.Coordinator.TryConsumeCompanion(permission.Notification.Key,
                permission.Notification.ObservedAtUtc, out _), "Tray click bypassed live application context permission.");
            applicationContext = true;

            var duePoll = PresentCompanion(later);
            var pollReminder = higherPriority with { Id = Guid.NewGuid(), DueAtUtc = duePoll.Notification.ObservedAtUtc };
            Require(scheduler.Upsert(pollReminder, out _), "Due-poll reminder fixture could not be saved.");
            Require(scheduler.MarkPresented(pollReminder.Id, pollReminder.DueAtUtc, out _), "Due-poll reminder presentation failed.");
            duePoll.Coordinator.Poll(pollReminder.DueAtUtc.AddSeconds(1));
            Require(cancellationEvents == 4, "Reminder in presentation cooldown did not cancel pending companion.");
            Require(scheduler.Acknowledge(pollReminder.Id, ScheduledReminderDisposition.Dismissed,
                pollReminder.DueAtUtc.AddSeconds(2), out _), "Due-poll reminder acknowledgement failed.");
            Require(!duePoll.Coordinator.TryConsumeCompanion(duePoll.Notification.Key,
                pollReminder.DueAtUtc.AddSeconds(2), out _), "Reminder-suppressed candidate returned after acknowledgement.");

            assistantBusy = true;
            var busyCoordinator = new AssistantNotificationCoordinator(scheduler, new LocalCompanionAdvisor(),
                capabilities, () => preferences, () => true, () => assistantBusy, () => context);
            int busySuggestions = 0;
            busyCoordinator.CompanionSuggestionReady += _ => busySuggestions++;
            busyCoordinator.Poll(later);
            busyCoordinator.Poll(later + CompanionSuggestionGate.StabilityDelay);
            Require(busySuggestions == 0, "Busy assistant produced proactive suggestion.");

            // Lock the temporary write destination to force a real persistence failure.
            string failurePath = Path.Combine(directory, "failure.json");
            var failureScheduler = new LocalSchedulerService(new LocalScheduleStore(failurePath));
            Require(failureScheduler.Upsert(reminder, out _), "Persistence failure reminder fixture could not be saved.");
            var failureCoordinator = new AssistantNotificationCoordinator(failureScheduler, new LocalCompanionAdvisor(),
                capabilities, () => preferences, () => true, () => true, () => context);
            int failureEvents = 0;
            Action<ScheduledReminderNotification> onFailure = notification =>
            {
                failureEvents++;
                Require(!failureCoordinator.MarkReminderPresented(notification.ScheduleId, notification.ObservedAtUtc,
                    out string error) && error.Length > 0, "Locked reminder presentation unexpectedly persisted.");
            };
            failureCoordinator.ReminderDue += onFailure;
            using (var locked = new FileStream(failurePath + ".tmp", FileMode.Create, FileAccess.Write, FileShare.None))
                failureCoordinator.Poll(now);
            failureCoordinator.Poll(now.AddSeconds(30));
            failureCoordinator.Poll(now + LocalSchedulerService.ReminderRecoveryDelay);
            failureCoordinator.Poll(now.AddHours(1));
            Require(failureEvents == 1 && failureScheduler.Schedules.Single().LastPresentedAtUtc is null,
                "Failed persistence caused reminder spam or changed in-memory state.");

            var restartedScheduler = new LocalSchedulerService(new LocalScheduleStore(failurePath));
            restartedScheduler.Load();
            var restarted = new AssistantNotificationCoordinator(restartedScheduler, new LocalCompanionAdvisor(),
                capabilities, () => preferences, () => true, () => true, () => context);
            int recoveredEvents = 0;
            restarted.ReminderDue += _ => recoveredEvents++;
            restarted.Poll(now.AddHours(1));
            Require(recoveredEvents == 1, "Failed-persistence reminder did not recover after restart.");
            Require(failureCoordinator.MarkReminderPresented(reminder.Id, now.AddHours(1), out _),
                "Presentation persistence could not recover after unlocking store.");
            failureCoordinator.ReminderDue -= onFailure;
            failureCoordinator.ReminderDue += _ => failureEvents++;
            failureCoordinator.Poll(now.AddHours(1) + LocalSchedulerService.ReminderRecoveryDelay - TimeSpan.FromTicks(1));
            Require(failureEvents == 1, "Recovered persistence ignored presentation cooldown.");
            failureCoordinator.Poll(now.AddHours(1) + LocalSchedulerService.ReminderRecoveryDelay);
            Require(failureEvents == 2, "Successful persistence retry did not clear session failure suppression.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
