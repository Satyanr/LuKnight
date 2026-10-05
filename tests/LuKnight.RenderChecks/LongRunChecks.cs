using System.IO;
using LuKnight;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private static void
        CheckConversationBounds()
    {
        var conversation =
            new ConversationManager();


        for (int i = 0;
             i < 180;
             i++)
        {
            conversation.AddUser(
                new AssistantRequest(
                    $"user-{i}"));

            conversation.AddAssistant(
                $"assistant-{i}");
        }


        Require(
            conversation.Count ==
                100,
            "Conversation transcript exceeded bounded session history.");


        Require(
            conversation
                .GetRecentContext()
                .Count ==
                20,
            "Provider context exceeded bounded short-term history.");


        Require(
            conversation
                .Turns[0]
                .Text
                .Contains(
                    "130",
                    StringComparison.Ordinal),
            "Conversation trimming did not keep newest turns.");
    }

    private sealed class
        CyclingBlockingTool :
            IAssistantTool, IDisposable
    {
        private readonly SemaphoreSlim
            _started =
                new(
                    0);


        public int Calls
        {
            get;
            private set;
        }


        public string Name =>
            BuiltInToolNames
                .MemoryRemember;


        public Task WaitStartedAsync() =>
            _started.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));

        public void Dispose() => _started.Dispose();


        public async Task<ToolExecutionResult>
            ExecuteAsync(
                ToolInvocation invocation,
                CancellationToken cancellationToken = default)
        {
            Calls++;

            _started.Release();


            await Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);


            return new(
                true,
                "Must not complete.");
        }
    }

    private static async Task
        CheckRepeatedCancellationAsync()
    {
        using var tool =
            new CyclingBlockingTool();


        var assistant =
            new AssistantController(
                new ChatCoordinator(
                    new FakeCredentials(),
                    new ChatSettings
                    {
                        Provider =
                            ChatProvider.Local
                    }),
                tools:
                    new AssistantToolRouter(
                        [
                            tool
                        ]));


        for (int i = 0;
             i < 32;
             i++)
        {
            using var cts =
                new CancellationTokenSource();


            Task<AssistantReply> operation =
                assistant.SendAsync(
                    new AssistantRequest(
                        $"ingat bahwa cancel-soak-{i}"),
                    cts.Token);


            await tool
                .WaitStartedAsync();


            cts.Cancel();


            bool cancelled =
                false;


            try
            {
                await operation.WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (
                OperationCanceledException)
            {
                cancelled =
                    true;
            }


            Require(
                cancelled,
                $"Cancellation cycle {i} completed unexpectedly.");


            Require(
                !assistant.IsBusy &&
                !assistant.HasPendingAction &&
                !assistant.HasPendingPlan,
                $"Cancellation cycle {i} retained runtime state.");
        }


        Require(
            assistant.Conversation
                .Count ==
                0,
            "Cancelled tool requests accumulated transcript state.");


        AssistantReply healthy =
            await assistant.SendAsync(
                new AssistantRequest(
                    "halo"));


        Require(
            healthy.Backend ==
                AssistantBackend.Local &&
            !assistant.IsBusy,
            "Assistant did not recover after repeated cancellation.");
    }

    private static void
        CheckCompanionLongRunBounds()
    {
        var gate =
            new CompanionSuggestionGate();


        DateTimeOffset now =
            new(
                2026,
                10,
                6,
                0,
                0,
                0,
                TimeSpan.Zero);


        CompanionSuggestionCandidate[] candidates =
        [
            new(
                "app:code-editor",
                CompanionSuggestionKind.Coding,
                "Coding",
                "Coding"),

            new(
                "app:browser",
                CompanionSuggestionKind.Browsing,
                "Browsing",
                "Browsing"),

            new(
                "app:office",
                CompanionSuggestionKind.Office,
                "Office",
                "Office"),

            new(
                "app:creative",
                CompanionSuggestionKind.Creative,
                "Creative",
                "Creative")
        ];


        for (int i = 0;
             i < 3;
             i++)
        {
            gate.Observe(
                candidates[i],
                now);


            now +=
                CompanionSuggestionGate
                    .StabilityDelay;


            CompanionSuggestionCandidate?
                ready =
                    gate.Observe(
                        candidates[i],
                        now);


            Require(
                ready is not null,
                $"Companion candidate {i} did not reach dwell.");


            Require(
                gate.MarkPresented(
                    ready!,
                    now),
                $"Companion candidate {i} was not committed.");


            now +=
                CompanionSuggestionGate
                    .GlobalCooldown +
                TimeSpan.FromSeconds(
                    1);
        }


        Require(
            gate.PresentedCount ==
                CompanionSuggestionGate
                    .MaxSuggestionsPerSession,
            "Companion session cap was incorrect.");


        gate.Observe(
            candidates[3],
            now);


        now +=
            CompanionSuggestionGate
                .StabilityDelay;


        Require(
            gate.Observe(
                candidates[3],
                now) is null,
            "Companion exceeded per-session presentation cap.");
    }

    private static void
        CheckSchedulerLongRun(
            string directory)
    {
        string path =
            Path.Combine(
                directory,
                "soak-schedules.json");


        var scheduler =
            new LocalSchedulerService(
                new LocalScheduleStore(
                    path));


        scheduler.Load();


        DateTimeOffset now =
            new(
                2026,
                10,
                6,
                0,
                0,
                0,
                TimeSpan.Zero);


        var schedule =
            new ScheduledSkill
            {
                Id =
                    Guid.NewGuid(),

                DisplayName =
                    "Long-run reminder",

                Enabled =
                    true,

                CreatedAtUtc =
                    now.AddHours(
                        -2),

                DueAtUtc =
                    now.AddHours(
                        -1),

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
                schedule,
                out _),
            "Long-run schedule fixture failed.");


        for (int i = 0;
             i < 24;
             i++)
        {
            scheduler = new LocalSchedulerService(new LocalScheduleStore(path));
            scheduler.Load();
            IReadOnlyList<ScheduledSkill>
                due =
                    scheduler
                        .GetReminderCandidates(
                            now);


            Require(
                due.Count ==
                    1 &&
                due[0].Id ==
                    schedule.Id,
                $"Reminder recovery cycle {i} duplicated/lost schedule.");


            Require(
                scheduler.MarkPresented(
                    schedule.Id,
                    now,
                    out _),
                $"Reminder cycle {i} could not persist presentation.");


            Require(
                scheduler
                    .GetReminderCandidates(
                        now.Add(
                            LocalSchedulerService
                                .ReminderRecoveryDelay -
                            TimeSpan.FromTicks(
                                1)))
                    .Count ==
                    0,
                $"Reminder cycle {i} ignored recovery cooldown.");


            now +=
                LocalSchedulerService
                    .ReminderRecoveryDelay;
        }


        Require(
            scheduler.Schedules.Count ==
                1,
            "Repeated reminder recovery duplicated schedules.");


        Require(
            scheduler.Acknowledge(
                schedule.Id,
                ScheduledReminderDisposition
                    .Dismissed,
                now,
                out _),
            "Long-run reminder could not be acknowledged.");


        Require(
            !scheduler.HasUnacknowledgedDue(
                now),
            "Acknowledged reminder remained active.");
    }

    private static void
        CheckPersistentRotation(
            string directory)
    {
        string path =
            Path.Combine(
                directory,
                "settings.json");


        var settings =
            new SettingsService(
                path);


        settings.Load();


        for (int i = 0;
             i < 48;
             i++)
        {
            Require(
                settings.Update(
                    settings.Current with
                    {
                        General =
                            settings.Current
                                .General with
                                {
                                    AlwaysOnTop =
                                        i % 2 ==
                                        0
                                }
                    }),
                $"Settings generation {i} failed.");


            Require(
                !File.Exists(
                    path + ".tmp") &&
                !File.Exists(
                    path + ".recover.tmp"),
                $"Settings generation {i} left uncommitted files.");
        }


        Require(
            File.Exists(
                path) &&
            File.Exists(
                path + ".bak"),
            "Settings generation rotation did not retain primary + backup.");


        var reload =
            new SettingsService(
                path);

        reload.Load();


        Require(
            reload.Current ==
                settings.Current,
            "Settings generation rotation produced unreadable final state.");
    }

    private static void
        CheckInvalidSnapshotRetention(
            string directory)
    {
        string path =
            Path.Combine(
                directory,
                "corrupt-settings.json");


        File.WriteAllText(
            path,
            "{ broken");


        for (int i = 0;
             i < 12;
             i++)
        {
            var settings =
                new SettingsService(
                    path);

            settings.Load();
        }


        string[] invalid =
            Directory.GetFiles(
                directory,
                "corrupt-settings.json.invalid-*.bak",
                SearchOption.TopDirectoryOnly);


        Require(
            invalid.Length <=
                3,
            "Invalid forensic snapshots grew without bound.");


        Require(
            invalid.Length >
                0,
            "Corrupt primary was never preserved.");
    }

    private static void
        CheckMemoryBounds()
    {
        var memory =
            new MemoryService();


        for (int i = 0;
             i < 260;
             i++)
        {
            memory.Remember(
                $"long-run-memory-{i}");
        }


        Require(
            memory.Count ==
                200,
            "Long-term memory exceeded bounded entry limit.");


        Require(
            memory.Items[0]
                .Text ==
                "long-run-memory-60",
            "Memory trimming did not retain newest bounded entries.");
    }

    private static void
        CheckRepeatedWindowLifecycle(
            string directory)
    {
        for (int i = 0;
             i < 12;
             i++)
        {
            var voice =
                new ShutdownVoiceCapture();

            var tts =
                new ShutdownTextToSpeech();


            var services =
                new AppServices(
                    credentials:
                        new FakeCredentials(),

                    voiceCapture:
                        voice,

                    textToSpeech:
                        tts,

                    desktopApps:
                        new MutablePlanAppCatalog(),

                    desktopWindows:
                        new FakeWindowCatalog(),

                    userSkillStore:
                        new UserSkillStore(
                            Path.Combine(
                                directory,
                                $"skills-{i}")),

                    scheduleStore:
                        new LocalScheduleStore(
                            Path.Combine(
                                directory,
                                $"schedule-{i}.json")));


            var window =
                new MainWindow(
                    services);


            window.BeginShutdown();

            window.Close();


            Require(
                voice.DisposeCalls ==
                    1 &&
                tts.DisposeCalls ==
                    1 &&
                services.Assistant
                    .IsShuttingDown,
                $"Window lifecycle {i} leaked runtime resources.");
        }
    }

    private static void CheckForensicRetentionIsolation(string directory, int snapshotYear = 2025)
    {
        string path = Path.Combine(directory, "retention-order.json");
        File.WriteAllText(path, "{ broken primary");
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.WriteAllText(path + ".bak", "{ broken committed backup");
        var old = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            string snapshot = path + $".invalid-seeded-{i}.bak";
            File.WriteAllText(snapshot, $"old-{i}");
            File.SetLastWriteTimeUtc(snapshot, new DateTime(snapshotYear, 1, 1, 0, 0, i, DateTimeKind.Utc));
            old.Add(snapshot);
        }
        string other = Path.Combine(directory, "other.json.invalid-probe.bak");
        File.WriteAllText(other, "other-state");
        string child = Path.Combine(directory, "child");
        Directory.CreateDirectory(child);
        string nested = Path.Combine(child, "retention-order.json.invalid-probe.bak");
        File.WriteAllText(nested, "nested-state");
        new SettingsService(path).Load();
        string[] retained = Directory.GetFiles(directory, "retention-order.json.invalid-*.bak");
        Require(retained.Length == 3 && File.Exists(old[4]) && File.Exists(old[3]) && !File.Exists(old[2]),
            "Forensic retention did not retain the newest generations.");
        Require(retained.Any(file => File.ReadAllText(file) == "{ broken primary"),
            "Forensic pruning removed the newly preserved corrupt primary.");
        Require(File.ReadAllText(path) == "{ broken primary" && File.ReadAllText(path + ".bak") == "{ broken committed backup",
            "Forensic pruning changed primary or committed backup.");
        Require(File.ReadAllText(other) == "other-state" && File.ReadAllText(nested) == "nested-state",
            "Forensic pruning crossed the state-file or directory boundary.");
    }

    private static async Task CheckRepeatedWorkflowAuthorizationAsync()
    {
        var apps = new MutablePlanAppCatalog();
        apps.Add(Installed("LongRunApp", "LongRunApp"));
        var action = new PlanTestAction(BuiltInActionNames.DesktopOpenApplication);
        var assistant = new AssistantController(new ChatCoordinator(new FakeCredentials(),
            new ChatSettings { Provider = ChatProvider.Local, UseDesktopActions = true }),
            intentRouter: new AssistantIntentRouter(new LocalDesktopCommandRouter(apps)),
            actions: new AssistantActionRouter([action]));
        int expectedExecutions = 0;
        for (int i = 0; i < 16; i++)
        {
            AssistantReply first = await assistant.SendAsync(new AssistantRequest("buka LongRunApp lalu buka LongRunApp"));
            Require(first.ActionProposal is not null && action.Executions == expectedExecutions,
                "Repeated workflow admission executed without confirmation.");
            Guid staleId = first.ActionProposal!.Id;
            if (i % 2 == 0) assistant.CancelAction(staleId);
            else
            {
                AssistantReply next = await assistant.ConfirmActionAsync(staleId);
                expectedExecutions++;
                Require(next.ActionProposal is not null && action.Executions == expectedExecutions,
                    "Workflow step execution duplicated or skipped authorization.");
                await assistant.ConfirmActionAsync(next.ActionProposal!.Id);
                expectedExecutions++;
            }
            bool rejected = false;
            try { await assistant.ConfirmActionAsync(staleId); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected && action.Executions == expectedExecutions && !assistant.IsBusy &&
                !assistant.HasPendingAction && !assistant.HasPendingPlan,
                "Repeated workflow completion/cancellation replayed authorization or retained state.");
        }
        Require(assistant.Conversation.Count <= 100 && assistant.Conversation.GetRecentContext().Count == 0,
            "Repeated private workflow traffic exceeded transcript bounds or entered provider context.");
        assistant.BeginShutdown();
    }

    private static async Task
        CheckLongRunRecoveryAsync()
    {
        string directory =
            Path.Combine(
                Path.GetTempPath(),
                "LuKnight-longrun-" +
                Guid.NewGuid()
                    .ToString("N"));


        Directory.CreateDirectory(
            directory);


        try
        {
            CheckConversationBounds();

            CheckMemoryBounds();

            CheckCompanionLongRunBounds();

            CheckSchedulerLongRun(
                directory);

            CheckPersistentRotation(
                directory);

            CheckInvalidSnapshotRetention(
                directory);

            CheckForensicRetentionIsolation(directory);
            await CheckRepeatedCancellationAsync();
            await CheckRepeatedWorkflowAuthorizationAsync();

            CheckRepeatedWindowLifecycle(
                directory);
        }
        finally
        {
            Directory.Delete(
                directory,
                recursive:
                    true);
        }
    }

}
