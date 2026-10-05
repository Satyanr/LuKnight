using System.IO;
using System.Windows.Controls;
using LuKnight.Services;
using LuKnight.ViewModels;
using LuKnight.Views;
using NAudio.Wave;

internal static partial class Program
{
    private static void CheckAuditRegressions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "LuKnight-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var failures = new List<string>();
        void Check(string name, Action test)
        {
            try { test(); }
            catch (Exception ex)
            {
                failures.Add(name);
                Console.WriteLine($"FAIL: {name} ({ex.GetType().Name}).");
            }
        }
        try
        {
            Check("forensic timestamp retention", () => CheckForensicRetentionIsolation(directory));
            Check("future snapshot timestamp retention", () => CheckForensicRetentionIsolation(directory, 2035));
            Check("voice disposal settles pending capture", CheckVoiceCaptureDisposal);
            Check("late recording event isolation", CheckLateRecordingEvent);
            Check("recording finalization failure", CheckRecordingFinalizationFailure);
            Check("bounded chat bubbles", CheckChatBubbleBounds);
            Check("settings disposal and closed commands", () => CheckSettingsModelDisposal(directory));
            Check("settings diagnostic privacy", CheckSettingsDiagnosticPrivacy);
            Require(failures.Count == 0, "Audit regressions failed: " + string.Join(", ", failures));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void CheckVoiceCaptureDisposal()
    {
        var capture = new VoiceCaptureService();
        var pending = new TaskCompletionSource<VoiceCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(VoiceCaptureService).GetField("_completion", Private)!.SetValue(capture, pending);
        capture.Dispose();
        Require(pending.Task.IsCanceled, "Disposing capture left its completion pending.");
        capture.Dispose();
        // A sentinel prevents opening hardware even if the disposed guard regresses.
        using var sentinel = new WaveIn();
        var inputField = typeof(VoiceCaptureService).GetField("_input", Private)!;
        inputField.SetValue(capture, sentinel);
        bool rejected = false;
        try { capture.Start(); }
        catch (ObjectDisposedException) { rejected = true; }
        finally { inputField.SetValue(capture, null); }
        Require(rejected, "Disposed capture retained permission to start a new session.");
    }

    private static void CheckLateRecordingEvent()
    {
        using var oldInput = new WaveIn();
        using var currentInput = new WaveIn();
        using var capture = new VoiceCaptureService();
        var pending = new TaskCompletionSource<VoiceCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(VoiceCaptureService).GetField("_input", Private)!.SetValue(capture, currentInput);
        typeof(VoiceCaptureService).GetField("_stream", Private)!.SetValue(capture, new MemoryStream());
        typeof(VoiceCaptureService).GetField("_completion", Private)!.SetValue(capture, pending);
        var complete = typeof(VoiceCaptureService).GetMethod("CompleteRecording", Private)!;
        // Simulate a queued stop event from the previous recorder, without opening a microphone.
        complete.Invoke(capture, [oldInput, null]);
        Require(capture.IsRecording && !pending.Task.IsCompleted,
            "A previous recording's queued callback completed/disposed the new session.");
    }

    private static void CheckRecordingFinalizationFailure()
    {
        using var input = new WaveIn();
        using var capture = new VoiceCaptureService();
        var stream = new MemoryStream();
        var writer = new WaveFileWriter(stream, new WaveFormat(16000, 16, 1));
        var pending = new TaskCompletionSource<VoiceCaptureResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        typeof(VoiceCaptureService).GetField("_input", Private)!.SetValue(capture, input);
        typeof(VoiceCaptureService).GetField("_stream", Private)!.SetValue(capture, stream);
        typeof(VoiceCaptureService).GetField("_writer", Private)!.SetValue(capture, writer);
        typeof(VoiceCaptureService).GetField("_completion", Private)!.SetValue(capture, pending);
        // Simulate an unusable output stream when the native stop callback finalizes the WAV.
        stream.Dispose();
        typeof(VoiceCaptureService).GetMethod("CompleteRecording", Private)!.Invoke(capture, [input, null]);
        Require(pending.Task.IsFaulted && pending.Task.Exception!.GetBaseException() is ObjectDisposedException &&
            !capture.IsRecording, "WAV finalization failure escaped the callback or left capture pending.");
    }

    private static void CheckChatBubbleBounds()
    {
        var panel = new ChatPanel();
        var messages = (StackPanel)panel.FindName("MessagesPanel");
        Guid reminder = Guid.NewGuid();
        panel.AddScheduledReminder(reminder, "Keep reminder", DateTimeOffset.UtcNow.AddHours(-1));
        for (int i = 0; i < 180; i++)
        {
            panel.AddUserMessage($"user-{i}");
            panel.AddAssistantMessage($"assistant-{i}");
        }
        Require(messages.Children.Count == 101, "Visible chat history grew beyond 100 bubbles plus the reminder.");
        var bubbles = messages.Children.OfType<Border>().Where(border => border.Child is TextBlock).ToArray();
        Require(((TextBlock)bubbles[0].Child).Text == "user-130" &&
            ((TextBlock)bubbles[^1].Child).Text == "assistant-179", "Chat trimming lost the newest messages.");
        panel.ClearConversation();
        Require(messages.Children.Count == 1, "Chat trimming/clear removed the pending reminder.");
        panel.AddAssistantMessage("after-clear");
        Require(messages.Children.Count == 2, "Chat clear retained old bubble tracking.");
    }

    private static void CheckSettingsModelDisposal(string directory)
    {
        var services = new AppServices(credentials: new FakeCredentials(), voiceCapture: new ShutdownVoiceCapture(),
            textToSpeech: new ShutdownTextToSpeech(), desktopApps: new MutablePlanAppCatalog(), desktopWindows: new FakeWindowCatalog(),
            userSkillStore: new UserSkillStore(Path.Combine(directory, "skills")),
            scheduleStore: new LocalScheduleStore(Path.Combine(directory, "schedules.json")));
        var model = new ProductSettingsViewModel(services, () => { }, () => true, () => true, () => { });
        try
        {
            using var callbackModel = new ProductSettingsViewModel(services, () => { }, () => true, () => true, () => { });
            var lifetime = (CancellationTokenSource)typeof(ProductSettingsViewModel).GetField("_lifetime", Private)!.GetValue(callbackModel)!;
            CancellationToken token = lifetime.Token;
            using var callback = token.Register(() => throw new InvalidOperationException("Synthetic callback failure"));
            callbackModel.Dispose();
            Require(token.IsCancellationRequested && !callbackModel.CheckCommand.CanExecute(null),
                "Failing cancellation callback interrupted Settings disposal.");
            model.Dispose();
            model.Dispose();
            Require(!model.CheckCommand.CanExecute(null) && !model.SaveCommand.CanExecute(null) &&
                !model.CanEditChat && !model.CanDeleteVoiceModel, "Disposed Settings still accepted commands.");
        }
        finally
        {
            model.Dispose();
            services.Assistant.BeginShutdown();
            services.VoiceCapture.Dispose();
            services.TextToSpeech.Dispose();
        }
    }

    private static void CheckSettingsDiagnosticPrivacy()
    {
        string root = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(root, "LuKnight.csproj"))) root = Directory.GetParent(root)!.FullName;
        foreach (string name in new[] { "ProductSettingsViewModel.cs", "SettingsViewModel.cs" })
        {
            string source = File.ReadAllText(Path.Combine(root, "ViewModels", name));
            Require(source.Contains("DiagnosticPrivacy.TraceFailure", StringComparison.Ordinal) &&
                !source.Contains("\" + ex", StringComparison.Ordinal), "Settings diagnostics log full exception data.");
        }
    }
}
