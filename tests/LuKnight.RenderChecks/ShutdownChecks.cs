using System.Windows;
using System.Windows.Controls;
using LuKnight;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private static async Task RequireShutdownCancellation(Task task, string message)
    {
        bool cancelled = false;
        try { await task.WaitAsync(TimeSpan.FromSeconds(10)); }
        catch (OperationCanceledException) { cancelled = true; }
        Require(cancelled, message);
    }

    private sealed class ShutdownBlockingTool : IAssistantTool
    {
        public string Name => BuiltInToolNames.MemoryRemember;
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ToolExecutionResult> ExecuteAsync(ToolInvocation invocation, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult(true);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(true, "Must not complete.");
        }
    }

    private static async Task CheckShutdownAsync()
    {
        var blocking = new ShutdownBlockingTool();
        var assistant = new AssistantController(new ChatCoordinator(new FakeCredentials(),
            new ChatSettings { Provider = ChatProvider.Local }), tools: new AssistantToolRouter([blocking]));
        Task<AssistantReply> active = assistant.SendAsync(new AssistantRequest("ingat bahwa shutdown-test"));
        await blocking.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        assistant.BeginShutdown();
        assistant.BeginShutdown();
        Require(assistant.IsShuttingDown, "Assistant did not enter shutdown state.");
        await RequireShutdownCancellation(active, "Active assistant request survived shutdown.");
        await RequireShutdownCancellation(assistant.SendAsync(new AssistantRequest("request setelah shutdown")),
            "Assistant accepted new work after shutdown.");

        var apps = new MutablePlanAppCatalog();
        apps.Add(Installed("RecoveryApp", "RecoveryApp"));
        foreach (bool plan in new[] { false, true })
        foreach (bool sensitive in new[] { false, true })
        foreach (bool final in sensitive ? new[] { false, true } : new[] { false })
        {
            var action = new PlanTestAction(BuiltInActionNames.DesktopOpenApplication)
                { Risk = sensitive ? AssistantActionRisk.Sensitive : AssistantActionRisk.Navigation };
            var pendingAssistant = new AssistantController(new ChatCoordinator(new FakeCredentials(),
                new ChatSettings { Provider = ChatProvider.Local, UseDesktopActions = true }),
                intentRouter: new AssistantIntentRouter(new LocalDesktopCommandRouter(apps, new FakeWindowCatalog())),
                actions: new AssistantActionRouter([action], () => DesktopPermissionLevel.Sensitive));
            AssistantReply pending = await pendingAssistant.SendAsync(new AssistantRequest(
                plan ? "buka RecoveryApp lalu buka RecoveryApp" : "buka RecoveryApp"));
            Require(pending.ActionProposal is not null && pendingAssistant.HasPendingPlan == plan,
                "Shutdown confirmation fixture failed.");
            if (final)
            {
                pending = await pendingAssistant.ConfirmActionAsync(pending.ActionProposal!.Id);
                Require(pending.ActionProposal?.ConfirmationStage == AssistantConfirmationStage.SensitiveFinal,
                    "Shutdown final confirmation fixture failed.");
            }
            pendingAssistant.BeginShutdown();
            Require(!pendingAssistant.HasPendingAction && !pendingAssistant.HasPendingPlan,
                "Shutdown retained pending authorization.");
            await RequireShutdownCancellation(pendingAssistant.ConfirmActionAsync(pending.ActionProposal!.Id),
                "Shutdown accepted stale confirmation.");
            await RequireShutdownCancellation(pendingAssistant.StartScheduledSkillAsync(new ScheduledSkill()),
                "Shutdown accepted scheduled handoff.");
            Require(action.Executions == 0, "Shutdown confirmation reached native executor.");
        }

        foreach (bool plan in new[] { false, true })
        {
            AssistantController? racing = null;
            var action = new PlanTestAction(BuiltInActionNames.DesktopOpenApplication)
                { OnPrepare = () => racing!.BeginShutdown() };
            racing = new AssistantController(new ChatCoordinator(new FakeCredentials(),
                new ChatSettings { Provider = ChatProvider.Local, UseDesktopActions = true }),
                intentRouter: new AssistantIntentRouter(new LocalDesktopCommandRouter(apps)),
                actions: new AssistantActionRouter([action], () => DesktopPermissionLevel.Sensitive));
            await RequireShutdownCancellation(racing.SendAsync(new AssistantRequest(
                plan ? "buka RecoveryApp lalu buka RecoveryApp" : "buka RecoveryApp")),
                "Preparation continuation survived shutdown.");
            Require(!racing.HasPendingAction && !racing.HasPendingPlan && action.Executions == 0,
                "Preparation restored authorization after shutdown.");
        }

        // A collaborator may finish despite cancellation. It must not republish
        // the next workflow step after shutdown clears the current session.
        AssistantController? finishing = null;
        var finishingAction = new PlanTestAction(BuiltInActionNames.DesktopOpenApplication)
            { OnExecute = () => finishing!.BeginShutdown() };
        finishing = new AssistantController(new ChatCoordinator(new FakeCredentials(),
            new ChatSettings { Provider = ChatProvider.Local, UseDesktopActions = true }),
            intentRouter: new AssistantIntentRouter(new LocalDesktopCommandRouter(apps)),
            actions: new AssistantActionRouter([finishingAction]));
        AssistantReply first = await finishing.SendAsync(new AssistantRequest("buka RecoveryApp lalu buka RecoveryApp"));
        await RequireShutdownCancellation(finishing.ConfirmActionAsync(first.ActionProposal!.Id),
            "Late execution result survived shutdown.");
        Require(!finishing.HasPendingAction && !finishing.HasPendingPlan && finishingAction.Preparations == 1 &&
            finishingAction.Executions == 1, "Late completion restored workflow state or prepared another step.");

        CheckWindowShutdown();
    }

    private static void CheckWindowShutdown()
    {
        string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "LuKnight-shutdown-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            foreach (bool failCleanup in new[] { false, true })
            {
                var voice = new ShutdownVoiceCapture { ThrowOnDispose = failCleanup };
                var tts = new ShutdownTextToSpeech { ThrowOnStop = failCleanup };
                var services = new AppServices(credentials: new FakeCredentials(), voiceCapture: voice, textToSpeech: tts,
                    desktopApps: new MutablePlanAppCatalog(), desktopWindows: new FakeWindowCatalog(),
                    userSkillStore: new UserSkillStore(System.IO.Path.Combine(directory, "skills")),
                    scheduleStore: new LocalScheduleStore(System.IO.Path.Combine(directory, "schedules.json")));
                var window = new MainWindow(services);
                using var request = new CancellationTokenSource();
                using var transcription = new CancellationTokenSource();
                using var speech = new CancellationTokenSource();
                using var voiceLimit = new CancellationTokenSource();
                using var callback = request.Token.Register(() => { if (failCleanup) throw new InvalidOperationException("test callback"); });
                foreach (var field in new[] { ("_requestCts", request), ("_transcriptionCts", transcription),
                    ("_speechCts", speech), ("_voiceLimitCts", voiceLimit) })
                    typeof(MainWindow).GetField(field.Item1, Private)!.SetValue(window, field.Item2);
                try
                {
                    tts.IsSpeaking = false;
                    Require(window.CanInstallUpdate, "Idle update fixture was not ready before shutdown.");
                    tts.IsSpeaking = true;
                    window.BeginShutdown();
                    tts.IsSpeaking = false; // Audio finishing must not reopen the update gate.
                    Require(!window.CanInstallUpdate, "Shutdown still allowed an update installer handoff.");
                    Require(window.IsShuttingDown && services.Assistant.IsShuttingDown,
                        "Window shutdown did not propagate to Assistant.");
                    Require(request.IsCancellationRequested && transcription.IsCancellationRequested &&
                        speech.IsCancellationRequested && voiceLimit.IsCancellationRequested,
                        "Window shutdown did not cancel all owned operations.");
                    Require(voice.DisposeCalls == 1 && tts.StopCalls == 1,
                        "Shutdown did not stop realtime audio resources.");
                    window.BeginShutdown();
                    Require(voice.DisposeCalls == 1 && tts.StopCalls == 1, "Shutdown was not idempotent.");
                    var panel = (LuKnight.Views.ChatPanel)window.FindName("ChatPanelControl");
                    int turns = services.Assistant.Conversation.Turns.Count;
                    panel.SetDraftMessage("request setelah shutdown");
                    ((Button)panel.FindName("SendButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(services.Assistant.Conversation.Turns.Count == turns,
                        "Detached UI submitted assistant work after shutdown.");
                }
                finally { window.Close(); }
                Require(voice.DisposeCalls == 1 && tts.DisposeCalls == 1,
                    "Window close skipped disposal or disposed resources twice.");
                Require(!request.TryReset() && !transcription.TryReset() && !speech.TryReset(),
                    "Operation-owned cancellation sources were disposed or reset by window close.");
            }
        }
        finally { System.IO.Directory.Delete(directory, recursive: true); }
    }

    private sealed class ShutdownVoiceCapture : IVoiceCaptureService
    {
        public int DisposeCalls { get; private set; }
        public bool ThrowOnDispose;
        public bool IsRecording => true;
        public void Start() { }
        public Task<VoiceCaptureResult> StopAsync(CancellationToken cancellationToken = default) =>
            Task.FromCanceled<VoiceCaptureResult>(cancellationToken.IsCancellationRequested ? cancellationToken : new CancellationToken(true));
        public void Dispose()
        {
            DisposeCalls++;
            if (ThrowOnDispose) throw new InvalidOperationException("test disposal");
        }
    }

    private sealed class ShutdownTextToSpeech : ITextToSpeechService
    {
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public bool ThrowOnStop;
        public bool IsSpeaking { get; set; } = true;
        public IReadOnlyList<string> GetInstalledVoices() => [];
        public Task SpeakAsync(string text, TextToSpeechOptions options, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Stop()
        {
            StopCalls++;
            if (ThrowOnStop) throw new InvalidOperationException("test stop");
        }
        public void Dispose() { DisposeCalls++; }
    }
}
