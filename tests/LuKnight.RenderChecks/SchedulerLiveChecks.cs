using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;
using LuKnight.Views;

internal static partial class Program
{
    private static async Task
        CheckSchedulerLiveAsync()
    {
        DateTimeOffset now =
            DateTimeOffset.UtcNow;

        string token =
            Guid.NewGuid()
                .ToString("N")[..8];

        string initialTitle =
            $"{UiFixturePrefix} {token}";

        string refreshedTitle =
            $"{initialTitle} — REFRESH INVOKED";


        string directory =
            Path.Combine(
                Path.GetTempPath(),
                "LuKnight-SchedulerLive",
                Guid.NewGuid()
                    .ToString("N"));

        string skillDirectory =
            Path.Combine(
                directory,
                "skills");

        string schedulePath =
            Path.Combine(
                directory,
                "schedules.json");

        Directory.CreateDirectory(
            skillDirectory);


        //
        // Persist a real user skill.
        //

        var definition =
            new UserSkillDefinition
            {
                SchemaVersion =
                    2,

                Id =
                    "scheduled-native-refresh",

                DisplayName =
                    "Scheduled Native Refresh",

                Description =
                    "Native scheduler acceptance skill.",

                Parameters =
                [
                    new()
                    {
                        Name =
                            "window",

                        Required =
                            true,

                        MaxLength =
                            200
                    }
                ],

                Steps =
                [
                    "klik tombol Refresh di window {window}"
                ]
            };


        File.WriteAllText(
            Path.Combine(
                skillDirectory,
                "scheduled-native-refresh.json"),
            JsonSerializer.Serialize(
                definition,
                SettingsService.JsonOptions));


        using Process fixture =
            StartUiFixtureProcess(
                token);


        try
        {
            var windows =
                new DesktopWindowTargetService();

            await WaitForFixtureWindowAsync(
                windows,
                fixture,
                initialTitle);


            //
            // ------------------------------------------------
            // Persist schedule that was presented before
            // an imagined app shutdown, but never acknowledged.
            // ------------------------------------------------
            //

            var originalScheduler =
                new LocalSchedulerService(
                    new LocalScheduleStore(
                        schedulePath));


            var scheduled =
                new ScheduledSkill
                {
                    Id =
                        Guid.NewGuid(),

                    Enabled =
                        true,

                    DisplayName =
                        "Refresh test window",

                    CreatedAtUtc =
                        now.AddHours(
                            -1),

                    DueAtUtc =
                        now.AddMinutes(
                            -10),

                    // It was presented six minutes ago,
                    // then app supposedly terminated.
                    LastPresentedAtUtc =
                        now.AddMinutes(
                            -6),

                    AcknowledgedAtUtc =
                        null,

                    Disposition =
                        null,

                    Invocation =
                        new ScheduledSkillInvocation
                        {
                            SkillId =
                                "scheduled-native-refresh",

                            Parameters =
                                new Dictionary<string, string>
                                {
                                    ["window"] =
                                        initialTitle
                                }
                        }
                };


            Require(
                originalScheduler.Upsert(
                    scheduled,
                    out string saveError),
                $"Native scheduler fixture could not be persisted: {saveError}");


            //
            // Simulate restart.
            //

            var scheduler =
                new LocalSchedulerService(
                    new LocalScheduleStore(
                        schedulePath));

            scheduler.Load();


            ScheduledSkill recovered =
                scheduler
                    .GetReminderCandidates(
                        now)
                    .SingleOrDefault(
                        item =>
                            item.Id ==
                                scheduled.Id)
                ?? throw new InvalidOperationException(
                    "Presented-but-unacknowledged schedule was not recovered after restart.");


            Require(
                recovered.AcknowledgedAtUtc is null &&
                recovered.Disposition is null,
                "Recovered reminder was already acknowledged.");


            //
            // ------------------------------------------------
            // Load real persisted user skill.
            // ------------------------------------------------
            //

            UserSkillLoadResult loaded =
                new UserSkillStore(
                    skillDirectory)
                .Load();


            Require(
                loaded.Skills.Count ==
                    1 &&
                loaded.Issues.Count ==
                    0,
                "Native scheduled skill JSON was not loaded.");


            var skills =
                new AssistantSkillRouter(
                    BuiltInSkillCatalog.Create());


            foreach (IAssistantSkill skill
                     in loaded.Skills)
            {
                skills.Register(
                    skill);
            }


            //
            // ------------------------------------------------
            // Native desktop action stack.
            // ------------------------------------------------
            //

            var ui =
                new WindowsDesktopUiAutomationReader();

            var sequence =
                new List<string>();

            var invoke =
                new RecordingUiActionExecutor(
                    new WindowsDesktopUiActionExecutor(),
                    sequence);

            var mouse =
                new RecordingMouseActionExecutor(
                    new WindowsDesktopMouseActionExecutor(),
                    sequence);


            using var handler =
                new FakeHttp(
                    (_, _) =>
                        throw new InvalidOperationException(
                            "Native scheduler acceptance called Gemini."));

            using var client =
                new HttpClient(
                    handler);


            var chat =
                new ChatCoordinator(
                    new FakeCredentials
                    {
                        Key =
                            "unused-scheduler-live-key"
                    },
                    new ChatSettings
                    {
                        Provider =
                            ChatProvider.Gemini,

                        UseDesktopActions =
                            true,

                        DesktopPermission =
                            DesktopPermissionLevel
                                .Sensitive
                    },
                    () => null,
                    client);


            var intentRouter =
                new AssistantIntentRouter(
                    new LocalDesktopCommandRouter(
                        new DesktopAppCatalogService(
                            () =>
                                Array.Empty<
                                    DesktopAppTarget>()),
                        windows));


            var action =
                new InvokeDesktopUiControlAction(
                    () =>
                        chat.Options
                            .UseDesktopActions,
                    windows,
                    ui,
                    invoke,
                    mouse);


            var assistant =
                new AssistantController(
                    chat,
                    intentRouter:
                        intentRouter,
                    actions:
                        new AssistantActionRouter(
                            new IAssistantAction[]
                            {
                                action
                            },
                            () =>
                                chat.Options
                                    .DesktopPermission),
                    skills:
                        skills,
                    workflowRuntime:
                        new AssistantWorkflowRuntime(),
                    clock:
                        () => now);


            //
            // ------------------------------------------------
            // Tray → typed reminder ID → ChatPanel.
            // ------------------------------------------------
            //

            var panel =
                new ChatPanel();


            Guid? requestedRun =
                null;

            panel.ScheduledReminderRunRequested +=
                id =>
                    requestedRun =
                        id;


            using var tray =
                new TrayIconService(
                    toggle:
                        () => { },

                    show:
                        () => { },

                    chat:
                        () => { },

                    settings:
                        () => { },

                    restart:
                        () => { },

                    exit:
                        () => { },

                    reminder:
                        scheduleId =>
                        {
                            Require(
                                scheduler.TryGetDue(
                                    scheduleId,
                                    now,
                                    out ScheduledSkill due),
                                "Tray reminder resolved a non-due or acknowledged schedule.");

                            panel.AddScheduledReminder(
                                due.Id,
                                due.DisplayName,
                                due.DueAtUtc);
                        });


            tray.Show();

            //
            // Merely being due must not execute anything.
            //

            Require(
                invoke.Calls == 0 &&
                mouse.Calls == 0 &&
                !assistant.HasPendingPlan &&
                !assistant.HasPendingAction,
                "Recovered schedule executed before notification interaction.");


            //
            // Present notification.
            //

            Require(
                tray.NotifyReminder(
                    recovered.Id,
                    recovered.DisplayName),
                "Native reminder notification was rejected.");


            Require(
                scheduler.MarkPresented(
                    recovered.Id,
                    now,
                    out string presentationError),
                $"Native reminder presentation could not be persisted: {presentationError}");


            Require(
                invoke.Calls == 0 &&
                mouse.Calls == 0,
                "Showing a schedule notification executed desktop automation.");


            //
            // Simulate user clicking Windows tray balloon.
            //

            var icon =
                Get<System.Windows.Forms.NotifyIcon>(
                    tray,
                    "_icon");

            var balloonClick =
                typeof(System.Windows.Forms.NotifyIcon)
                    .GetMethod(
                        "OnBalloonTipClicked",
                        Private)
                ?? throw new MissingMethodException(
                    "NotifyIcon.OnBalloonTipClicked");


            balloonClick.Invoke(
                icon,
                Array.Empty<object>());


            var cards =
                Get<Dictionary<Guid, Border>>(
                    panel,
                    "_scheduledReminderCards");


            Require(
                cards.Count ==
                    1 &&
                cards.ContainsKey(
                    recovered.Id),
                "Tray reminder click did not create the correct reminder card.");


            Require(
                invoke.Calls == 0 &&
                mouse.Calls == 0 &&
                !assistant.HasPendingAction,
                "Opening a reminder card executed its workflow.");


            //
            // ------------------------------------------------
            // Click Run on the card.
            // ------------------------------------------------
            //

            Border card =
                cards[
                    recovered.Id];

            var cardContent =
                (StackPanel)card.Child;

            var buttons =
                (StackPanel)
                    cardContent.Children[2];

            var run =
                (Button)
                    buttons.Children[0];


            run.RaiseEvent(
                new RoutedEventArgs(
                    Button.ClickEvent));


            Require(
                requestedRun ==
                    recovered.Id,
                "Reminder Run lost the typed schedule ID.");


            Require(
                cards.ContainsKey(
                    recovered.Id),
                "Run consumed the reminder card before handoff.");


            Require(
                scheduler.TryGetDue(
                    requestedRun ?? throw new InvalidOperationException("Reminder Run returned no schedule ID."),
                    now,
                    out ScheduledSkill dueForRun),
                "Run could not resolve its scheduled workflow.");


            //
            // Run means START workflow, not execute action.
            //

            AssistantReply proposal =
                await assistant
                    .StartScheduledSkillAsync(
                        dueForRun);


            Require(
                proposal.ActionProposal is
                {
                    IsPlanStep:
                        true,

                    PlanStepNumber:
                        1,

                    PlanStepCount:
                        1,

                    Risk:
                        AssistantActionRisk.Interaction,

                    ConfirmationStage:
                        AssistantConfirmationStage.Standard
                },
                "Scheduled Run did not reach normal planner confirmation.");


            Require(
                invoke.Calls == 0 &&
                mouse.Calls == 0,
                "Scheduled Run executed native UI before action confirmation.");


            //
            // Persist user choice before allowing confirmation chain.
            //

            Require(
                scheduler.Acknowledge(
                    recovered.Id,
                    ScheduledReminderDisposition
                        .RunRequested,
                    now,
                    out string acknowledgementError),
                $"RunRequested acknowledgement failed: {acknowledgementError}");


            panel.RemoveScheduledReminder(
                recovered.Id);


            Require(
                cards.Count == 0,
                "Acknowledged reminder card remained visible.");


            //
            // ------------------------------------------------
            // Native execution still requires normal confirmation.
            // ------------------------------------------------
            //

            AssistantReply completed =
                await assistant
                    .ConfirmActionAsync(
                        proposal.ActionProposal!.Id);


            Require(
                completed.ActionProposal is null,
                "Scheduled native workflow returned an unexpected second proposal.");


            Require(
                invoke.Calls ==
                    1,
                "Scheduled native workflow did not invoke Refresh exactly once.");


            Require(
                mouse.Calls ==
                    0,
                "Scheduled native workflow unexpectedly used mouse fallback.");


            Require(
                sequence.SequenceEqual(
                    new[]
                    {
                        "uia"
                    }),
                "Scheduled native execution was not UIA-only.");


            await WaitForFixtureWindowAsync(
                windows,
                fixture,
                refreshedTitle);


            Require(
                !assistant.HasPendingPlan &&
                !assistant.HasPendingAction,
                "Scheduled workflow remained pending after native completion.");


            AssistantReply stale =
                await assistant.StartScheduledSkillAsync(
                    scheduler.Schedules
                        .Single(
                            item =>
                                item.Id ==
                                    recovered.Id));

            Require(
                stale.ActionProposal is null &&
                !assistant.HasPendingPlan &&
                !assistant.HasPendingAction,
                "Acknowledged schedule snapshot was executable again.");

            //
            // Acknowledged schedule cannot execute again.
            //

            Require(
                !scheduler.TryGetDue(
                    recovered.Id,
                    now.AddHours(
                        1),
                    out _),
                "RunRequested schedule remained executable.");


            Require(
                scheduler
                    .GetReminderCandidates(
                        now.AddDays(
                            10))
                    .All(
                        item =>
                            item.Id !=
                                recovered.Id),
                "RunRequested schedule returned as a reminder.");


            //
            // Restart again and verify acknowledgement persists.
            //

            var finalScheduler =
                new LocalSchedulerService(
                    new LocalScheduleStore(
                        schedulePath));

            finalScheduler.Load();


            ScheduledSkill persisted =
                finalScheduler
                    .Schedules
                    .Single(
                        item =>
                            item.Id ==
                                recovered.Id);


            Require(
                persisted.Disposition ==
                    ScheduledReminderDisposition
                        .RunRequested &&
                persisted.AcknowledgedAtUtc is not null,
                "Run acknowledgement did not survive scheduler restart.");


            Require(
                !finalScheduler.TryGetDue(
                    persisted.Id,
                    now.AddDays(
                        1),
                    out _),
                "Restart restored an acknowledged workflow as executable.");


            Require(
                finalScheduler
                    .GetReminderCandidates(
                        now.AddDays(
                            1))
                    .All(
                        item =>
                            item.Id !=
                                persisted.Id),
                "Restart restored an acknowledged workflow reminder.");


            Require(
                handler.Calls ==
                    0 &&
                assistant.Conversation
                    .GetRecentContext()
                    .Count ==
                    0,
                "Native scheduled workflow called Gemini or leaked context.");


            Console.WriteLine();
            Console.WriteLine(
                "Persisted reminder recovered after simulated restart.");

            Console.WriteLine(
                "Tray notification carried typed schedule ID.");

            Console.WriteLine(
                "Tray click created reminder card only.");

            Console.WriteLine(
                "Run created planner confirmation only.");

            Console.WriteLine(
                "RunRequested acknowledgement persisted.");

            Console.WriteLine(
                "Confirmation executed native Refresh through UIA.");

            Console.WriteLine(
                "Mouse fallback: 0.");

            Console.WriteLine(
                "Gemini calls: 0.");

            Console.WriteLine(
                "Acknowledged reminder remained consumed after restart.");

            Console.WriteLine();
            Console.WriteLine("Tray balloon click and card Run use synthetic events; shell display and physical clicks are not verified.");
            Console.WriteLine(
                "PASS: native scheduler acceptance.");
        }
        finally
        {
            await StopUiFixtureAsync(
                fixture);

            try
            {
                Directory.Delete(
                    directory,
                    recursive:
                        true);
            }
            catch (Exception ex)
                when (ex is
                    IOException or
                    UnauthorizedAccessException)
            {
                Console.WriteLine(
                    $"Warning: scheduler live cleanup failed: {ex.Message}");
            }
        }
    }
}
