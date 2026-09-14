using System.Net.Http;
using System.Windows;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private sealed class FakeUiTextExecutor : IDesktopUiTextActionExecutor
    {
        public int Calls;
        public string? LastValue;
        public (DesktopWindowTarget Window, string Path, string Fingerprint)? Target;
        public DesktopUiTextResult Result = DesktopUiTextResult.Set("Text set.");

        public Task<DesktopUiTextResult> SetTextAsync(
            DesktopWindowTarget window, string controlPath, string expectedFingerprint,
            string value, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastValue = value;
            Target = (window, controlPath, expectedFingerprint);
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeKeyboardTextExecutor : IDesktopKeyboardTextActionExecutor
    {
        public int Calls;
        public DesktopActionResult Result = new(true, "Keyboard text set.");
        public (DesktopWindowTarget Window, string Path, string Fingerprint, string Value)? Target;
        public Task<DesktopActionResult> ReplaceTextAsync(DesktopWindowTarget window,
            string controlPath, string expectedFingerprint, string value,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Target = (window, controlPath, expectedFingerprint, value);
            return Task.FromResult(Result);
        }
    }

    private static async Task CheckDesktopUiTextAsync()
    {
        var window = new DesktopWindowTarget((nint)100, 10, "notepad", "Notes", 0, false, true);
        var field = new DesktopUiNodeSnapshot("0/5", 1, "Edit", "Search", "SearchBox", "TextBox",
            new Rect(50, 100, 200, 30), true, false, false, false);
        var password = field with { Path = "0/6", Name = "[protected]", AutomationId = "", IsPassword = true };
        var secret = field with { Path = "0/7", Name = "API Key", AutomationId = "ApiKeyInput" };
        var snapshot = new DesktopUiSnapshot(window, new[] { field, password, secret }, false);
        Require(DesktopUiTextInputPolicy.ValidateTarget(field, out _), "Normal Edit blocked.");
        foreach (DesktopUiNodeSnapshot blocked in new[]
        {
            password, secret, field with { ControlType = "Button" },
            field with { IsEnabled = false }, field with { IsOffscreen = true }
        })
            Require(!DesktopUiTextInputPolicy.ValidateTarget(blocked, out _), "Unsafe text target accepted.");
        foreach (string name in new[] { "Password", "PIN", "OTP", "CVV", "API Key", "Verification Code", "kode keamanan" })
            Require(!DesktopUiTextInputPolicy.ValidateTarget(field with { Name = name }, out _),
                $"Sensitive field accepted: {name}");
        foreach (string id in new[] { "ApiKeyInput", "APIKeyInput", "PasswordBox", "otp_input", "PINInput" })
            Require(!DesktopUiTextInputPolicy.ValidateTarget(field with { AutomationId = id }, out _),
                $"Sensitive AutomationId accepted: {id}");
        foreach (string id in new[]
        {
            "Password1", "PIN2", "OTP6", "CVV2", "CVC3", "Token1", "Secret2",
            "ApiKey1", "APIKey2", "VerificationCode2", "RecoveryCode3"
        })
            Require(!DesktopUiTextInputPolicy.ValidateTarget(field with { AutomationId = id }, out _),
                $"Numbered sensitive AutomationId accepted: {id}");
        foreach (string safe in new[] { "Shipping", "Pinpoint", "Tokenizer", "Search1", "Address2", "Email3" })
            Require(DesktopUiTextInputPolicy.ValidateTarget(field with { Name = safe, AutomationId = safe }, out _),
                $"Safe numbered field was rejected: {safe}");
        foreach (string id in new[] { "ApiKey1", "VerificationCode2", "SecurityCode3", "RecoveryCode4", "BackupCode5" })
            Require(!DesktopUiTextInputPolicy.ValidateTarget(field with { AutomationId = id }, out _),
                $"Sensitive compound field escaped policy: {id}");
        foreach (string name in new[] { "Search", "Name", "Email", "Shipping" })
            Require(DesktopUiTextInputPolicy.ValidateTarget(field with { Name = name }, out _),
                $"Normal field rejected: {name}");
        foreach (string value in new[] { "", "a\nb", "a\rb", "a\tb", "a\0b", new string('x', 1001) })
            Require(!DesktopUiTextInputPolicy.ValidateValue(value, out _), "Invalid text accepted.");
        Require(DesktopUiTextInputPolicy.ValidateValue(new string('x', 1000), out _), "Text limit boundary rejected.");

        DesktopUiTextCommand? parsed = DesktopUiTextCommandParser.Parse(
            "isi textbox Search dengan Hello World 123 di window Notepad");
        Require(parsed is { ControlQuery: "search", Value: "Hello World 123", WindowQuery: "Notepad" },
            "Text parser changed case or target.");
        const string exactValue = "  Hello WORLD 123 Ω  ";
        foreach (string prefix in new[] { "isi textbox", "isi text box", "isi kolom", "set textbox", "set text box" })
        {
            parsed = DesktopUiTextCommandParser.Parse($"{prefix} Search dengan {exactValue} di window current");
            Require(parsed?.Value == exactValue && parsed.WindowQuery == "window aktif",
                "Text parser changed whitespace/Unicode or active-window alias.");
        }
        Require(DesktopUiTextCommandParser.Parse("isi textbox Search dengan pesan di window lama di window Notepad")?.Value ==
            "pesan di window lama", "Text parser did not use the final window delimiter.");

        var catalog = new FakeWindowCatalog();
        catalog.Windows.Add(window);
        var router = new AssistantIntentRouter(new LocalDesktopCommandRouter(
            new DesktopAppCatalogService(() => Array.Empty<DesktopAppTarget>()), catalog));
        var ui = new FakeUiAutomationReader { Snapshot = snapshot };
        var executor = new FakeUiTextExecutor();
        var keyboard = new FakeKeyboardTextExecutor();
        bool enabled = true;
        var action = new SetDesktopUiTextAction(() => enabled, catalog, ui, executor, keyboard);
        using var handler = new FakeHttp((_, _) => throw new InvalidOperationException("Text input reached Gemini."));
        using var client = new HttpClient(handler);
        var chat = new ChatCoordinator(new FakeCredentials { Key = "unused-text-test-key" },
            new ChatSettings { Provider = ChatProvider.Gemini, UseDesktopActions = true }, () => null, client);
        var assistant = new AssistantController(chat, intentRouter: router,
            actions: new AssistantActionRouter(new IAssistantAction[] { action }));
        string command = $"isi textbox Search dengan {exactValue} di window Notepad";
        AssistantIntent intent = router.Route(command);
        Require(intent.Kind == AssistantIntentKind.Action &&
            intent.Action is { Name: BuiltInActionNames.DesktopSetUiText, IncludeInContext: false },
            "Text command was not a private action.");
        AssistantReply proposal = await assistant.SendAsync(new AssistantRequest(command));
        Require(proposal.Backend == AssistantBackend.Local && proposal.ActionProposal is not null &&
            executor.Calls == 0 && handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
            "Text input escaped confirmation or privacy boundary.");
        Require(!proposal.ActionProposal!.ConfirmationText.Contains(exactValue, StringComparison.Ordinal) &&
            proposal.ActionProposal.ConfirmationText.Contains($"{exactValue.Length} karakter", StringComparison.Ordinal),
            "Text confirmation exposed content or lost length.");
        AssistantReply confirmed = await assistant.ConfirmActionAsync(proposal.ActionProposal.Id);
        Require(confirmed.Backend == AssistantBackend.Local && executor.Calls == 1 && keyboard.Calls == 0 && executor.LastValue == exactValue &&
            executor.Target == (window, field.Path, DesktopUiNodeIdentity.Fingerprint(field)) &&
            handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
            "Confirmed text lost exact value, identity, or privacy.");

        foreach (string target in new[] { "password", "API Key" })
        {
            string blockedCommand = $"isi textbox {target} dengan TestValue di window Notepad";
            Require(router.Route(blockedCommand).Kind == AssistantIntentKind.Action,
                "Protected target was filtered by parser instead of target validation.");
            AssistantReply blocked = await assistant.SendAsync(new AssistantRequest(blockedCommand));
            Require(blocked.ActionProposal is null && executor.Calls == 1 && keyboard.Calls == 0 && handler.Calls == 0 &&
                assistant.Conversation.GetRecentContext().Count == 0, "Sensitive field reached execution or Gemini.");
        }
        foreach (string badCommand in new[]
        {
            "isi textbox Search dengan a\nb di window Notepad",
            "isi textbox Search dengan a\tb di window Notepad",
            "isi textbox Search dengan " + new string('x', 1001) + " di window Notepad",
            "isi textbox Search dengan value"
        })
        {
            AssistantReply rejected = await assistant.SendAsync(new AssistantRequest(badCommand));
            Require(rejected.Backend == AssistantBackend.Local && rejected.ActionProposal is null &&
                handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
                "Invalid text command leaked into Gemini/context.");
        }
        foreach (DesktopUiNodeSnapshot changed in new[]
        {
            field with { Name = "Changed" }, field with { AutomationId = "Changed" },
            field with { Path = "0/99" }, field with { IsPassword = true },
            field with { IsEnabled = false }, field with { IsOffscreen = true }
        })
        {
            ui.Snapshot = snapshot;
            proposal = await assistant.SendAsync(new AssistantRequest(command));
            Require(proposal.ActionProposal is not null, "Stale test preparation failed.");
            ui.Snapshot = snapshot with { Nodes = new[] { changed } };
            await assistant.ConfirmActionAsync(proposal.ActionProposal!.Id);
            Require(executor.Calls == 1 && keyboard.Calls == 0 && handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
                "Changed text field reached executor or Gemini.");
        }
        ui.Snapshot = snapshot;
        ActionPreparationResult prepared = await action.PrepareAsync(intent.Action!);
        enabled = false;
        Require(!(await action.ExecuteAsync(prepared.Action!)).Success && executor.Calls == 1,
            "Disabled text action executed.");
        Require(!(await action.PrepareAsync(intent.Action!)).Success, "Disabled text action prepared.");
        enabled = true;
        foreach (string reason in new[] { "Read-only", "ValuePattern unavailable", "Timeout", "COM failure" })
        {
            int before = executor.Calls;
            executor.Result = DesktopUiTextResult.Rejected(reason);
            ActionExecutionResult failed = await action.ExecuteAsync(prepared.Action!);
            Require(!failed.Success && failed.Message == reason && executor.Calls == before + 1,
                "Text executor failure retried or was not preserved.");
        }
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        int calls = executor.Calls;
        try
        {
            await action.ExecuteAsync(prepared.Action!, cancelled.Token);
            Require(false, "Cancelled text action executed.");
        }
        catch (OperationCanceledException) { }
        Require(executor.Calls == calls, "Cancelled text input reached executor.");

        Require(prepared.Action!.ConfirmationText.Contains("keyboard", StringComparison.OrdinalIgnoreCase),
            "Keyboard fallback was not disclosed.");
        foreach (DesktopUiTextResult outcome in new[]
        {
            DesktopUiTextResult.Set("Set."),
            DesktopUiTextResult.Rejected("ValuePattern unavailable."),
            DesktopUiTextResult.Rejected("Read-only."),
            DesktopUiTextResult.Indeterminate("Timeout."),
            DesktopUiTextResult.Indeterminate("COM failure."),
            new DesktopUiTextResult((DesktopUiTextOutcome)999, "Unknown.")
        })
        {
            executor.Result = outcome;
            int uiBefore = executor.Calls;
            int keyboardBefore = keyboard.Calls;
            ActionExecutionResult result = await action.ExecuteAsync(prepared.Action!);
            Require(result.Success == outcome.Success && executor.Calls == uiBefore + 1 &&
                keyboard.Calls == keyboardBefore, $"Keyboard escaped typed boundary: {outcome.Outcome}");
        }
        executor.Result = DesktopUiTextResult.Unsupported("ValuePattern unavailable.");
        int uiBeforeFallback = executor.Calls;
        int keyboardBeforeFallback = keyboard.Calls;
        ActionExecutionResult fallback = await action.ExecuteAsync(prepared.Action!);
        Require(fallback.Success && executor.Calls == uiBeforeFallback + 1 && keyboard.Calls == keyboardBeforeFallback + 1 &&
            keyboard.Target == (window, field.Path, DesktopUiNodeIdentity.Fingerprint(field), exactValue),
            "Unsupported ValuePattern did not use exactly one keyboard fallback with exact target/text.");
        var noKeyboard = new SetDesktopUiTextAction(() => true, catalog, ui, executor);
        Require(!(await noKeyboard.ExecuteAsync(prepared.Action!)).Success,
            "Missing keyboard executor did not fail safely.");
        keyboard.Result = new(false, "Focus changed.");
        int beforeRejectedKeyboard = keyboard.Calls;
        ActionExecutionResult rejectedKeyboard = await action.ExecuteAsync(prepared.Action!);
        Require(!rejectedKeyboard.Success && keyboard.Calls == beforeRejectedKeyboard + 1 &&
            rejectedKeyboard.Message.Contains("Focus changed.", StringComparison.Ordinal),
            "Keyboard validation failure was retried or lost.");
        foreach (string sensitive in new[] { "Password", "PIN", "OTP", "API Key", "CVV", "Token" })
        {
            ui.Snapshot = snapshot with { Nodes = new[] { field with { Name = sensitive } } };
            int uiBefore = executor.Calls;
            int keyboardBefore = keyboard.Calls;
            AssistantReply blocked = await assistant.SendAsync(new AssistantRequest(
                $"isi textbox {sensitive} dengan harmless di window Notepad"));
            Require(blocked.ActionProposal is null && executor.Calls == uiBefore && keyboard.Calls == keyboardBefore &&
                handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
                $"Sensitive field reached text or keyboard executor: {sensitive}");
        }
        foreach (string unsupported in new[]
        {
            "tekan Ctrl+A", "tekan Enter", "tekan tombol A", "shortcut Ctrl+S", "ketik di posisi cursor"
        })
            Require(router.Route(unsupported).Kind != AssistantIntentKind.Action,
                $"Arbitrary keyboard command became executable: {unsupported}");

        Type nativeInput = typeof(WindowsDesktopKeyboardTextActionExecutor).GetNestedType(
            "Input", System.Reflection.BindingFlags.NonPublic)!;
        Require(System.Runtime.InteropServices.Marshal.SizeOf(nativeInput) == (IntPtr.Size == 8 ? 40 : 28),
            "Keyboard INPUT struct does not match native architecture size.");
    }
}
