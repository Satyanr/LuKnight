using System.Net.Http;
using System.Windows;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private sealed class FakeUiActionExecutor : IDesktopUiActionExecutor
    {
        public int Calls;

        public DesktopUiInvokeResult Result = DesktopUiInvokeResult.Invoked("Button invoked.");

        public Task<DesktopUiInvokeResult> InvokeAsync(
            DesktopWindowTarget window,
            string controlPath,
            string expectedFingerprint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class FakeMouseActionExecutor : IDesktopMouseActionExecutor
    {
        public int Calls;
        public DesktopActionResult Result = new(true, "Mouse clicked.");
        public (DesktopWindowTarget Window, string Path, string Fingerprint)? Target;

        public Task<DesktopActionResult> ClickAsync(
            DesktopWindowTarget window,
            string controlPath,
            string expectedFingerprint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            Target = (window, controlPath, expectedFingerprint);
            return Task.FromResult(Result);
        }
    }

    private static async Task CheckDesktopUiResolverAsync()
    {
        await CheckDesktopUiTextAsync();
        Require(
            DesktopMouseGeometry.TryGetCenter(
                new Rect(100, 200, 80, 40),
                out Point center) &&
            center.X == 140 &&
            center.Y == 220,
            "Safe mouse center calculation failed.");
        Require(
            !DesktopMouseGeometry.TryGetCenter(Rect.Empty, out _),
            "Safe mouse accepted an empty rectangle.");
        Require(
            !DesktopMouseGeometry.TryGetCenter(new Rect(10, 10, 2, 2), out _),
            "Safe mouse accepted an unsafe tiny target.");

        DesktopWindowTarget window = new(
            (nint)100, 10, "notepad", "Notes", 0, false, true);
        DesktopUiSnapshot snapshot = new(
            window,
            new DesktopUiNodeSnapshot[]
            {
                new("0/0", 1, "Button", "Save", "SaveButton", "Button", Rect.Empty, true, false, false, false),
                new("0/1", 1, "Button", "Save As", "SaveAsButton", "Button", Rect.Empty, true, false, false, false),
                new("0/2", 1, "Edit", "Search", "SearchBox", "Edit", Rect.Empty, true, false, true, false),
                new("0/3", 1, "Edit", "[protected]", "", "", Rect.Empty, true, false, false, true),
                new("0/4", 1, "Button", "Refresh", "RefreshButton", "Button", Rect.Empty, true, false, false, false)
            },
            false);

        DesktopUiControlResolution save =
            DesktopUiControlResolver.Resolve(snapshot, "Save", "Button");
        Require(save.Match?.Name == "Save", "Exact button was not resolved.");
        Require(
            DesktopUiNodeIdentity.Fingerprint(snapshot.Nodes[0]) ==
            DesktopUiNodeIdentity.Fingerprint(
                snapshot.Nodes[0] with { Bounds = new Rect(20, 30, 80, 40) }),
            "Control identity changed when only bounds changed.");

        string longMetadata = new('A', 300);
        Require(
            DesktopUiText.Normalize(longMetadata).Length == DesktopUiText.DefaultMaxLength,
            "UI metadata normalization exceeded the fingerprint limit.");
        Require(
            DesktopUiText.Normalize("Hello\r\nWorld") == "Hello  World",
            "UI metadata normalization diverged from reader behavior.");

        DesktopUiNodeSnapshot refresh = snapshot.Nodes.First(x => x.Name == "Refresh");
        Require(
            !DesktopUiActionPolicy.IsTemporarilyBlocked(refresh, out _),
            "Safe Refresh button was blocked.");
        DesktopUiNodeSnapshot saveButton = snapshot.Nodes.First(x => x.Name == "Save");
        Require(
            DesktopUiActionPolicy.IsTemporarilyBlocked(saveButton, out _),
            "Save button bypassed temporary mutation policy.");
        foreach (string dangerous in new[]
        {
            "Delete", "Remove", "Send", "Submit", "Buy", "Pay", "Close", "OK",
            "Yes", "Simpan", "Hapus", "Kirim"
        })
        {
            Require(
                DesktopUiActionPolicy.IsTemporarilyBlocked(
                    refresh with { Name = dangerous },
                    out _),
                $"Sensitive button escaped policy: {dangerous}");
        }
        foreach (string disguised in new[]
        {
            "Save...", "Save As…", "&Save", "_Save", "Delete…", "&Delete",
            "OK!", "&Kirim", "Hapus...", "_Simpan"
        })
        {
            Require(
                DesktopUiActionPolicy.IsTemporarilyBlocked(
                    refresh with { Name = disguised },
                    out _),
                $"Decorated sensitive button escaped policy: {disguised}");
        }
        foreach (string dangerous in new[]
        {
            "Discard", "Clear", "Rename", "Move", "Archive", "Install", "Update",
            "Accept", "Allow", "Authorize", "Download", "Export", "Import",
            "Restart", "Shutdown", "Finish"
        })
        {
            Require(
                DesktopUiActionPolicy.IsTemporarilyBlocked(
                    refresh with { Name = dangerous },
                    out _),
                $"Sensitive button escaped policy: {dangerous}");
        }
        foreach (string safe in new[] { "Refresh", "Reload", "Back", "Previous" })
        {
            Require(
                !DesktopUiActionPolicy.IsTemporarilyBlocked(
                    refresh with { Name = safe },
                    out _),
                $"Safe navigation button was blocked: {safe}");
        }
        Require(
            DesktopUiActionPolicy.IsTemporarilyBlocked(refresh with { Name = "" }, out _),
            "Unnamed UI button became executable.");

        DesktopUiControlResolution search =
            DesktopUiControlResolver.Resolve(snapshot, "search", "textbox");
        Require(search.Match?.AutomationId == "SearchBox", "Textbox alias was not resolved.");

        DesktopUiControlResolution broad =
            DesktopUiControlResolver.Resolve(snapshot, "save", "Button");
        Require(broad.Match?.Name == "Save", "Exact control name did not outrank contains match.");

        DesktopUiControlResolution password =
            DesktopUiControlResolver.Resolve(snapshot, "protected", "Edit");
        Require(!password.Found && !password.Ambiguous, "Protected control entered resolver results.");

        IReadOnlyList<DesktopUiNodeSnapshot> buttons =
            DesktopUiControlResolver.List(snapshot, "tombol");
        Require(
            buttons.Count == 3 && buttons.All(x => x.ControlType == "Button"),
            "Control-type list filtering failed.");

        DesktopUiQueryCommand? find = DesktopUiQueryCommandParser.Parse(
            "cari tombol Save di window Notepad");
        Require(
            find is not null &&
            find.Mode == DesktopUiQueryMode.Find &&
            find.ControlType == "Button" &&
            find.Query == "save" &&
            find.WindowQuery == "notepad",
            "UI control find command was not parsed.");

        DesktopUiQueryCommand? list = DesktopUiQueryCommandParser.Parse(
            "ada tombol apa di window aktif");
        Require(
            list is not null &&
            list.Mode == DesktopUiQueryMode.List &&
            list.WindowQuery == "window aktif",
            "UI control list command was not parsed.");
        Require(
            DesktopUiQueryCommandParser.Parse("apakah tombol ini bagus") is null,
            "Ordinary conversation was captured by the UI query parser.");

        var catalog = new FakeWindowCatalog();
        catalog.Windows.Add(window);
        var desktopRouter = new LocalDesktopCommandRouter(
            new DesktopAppCatalogService(() => Array.Empty<DesktopAppTarget>()),
            catalog);
        var router = new AssistantIntentRouter(desktopRouter);
        foreach (string unsupported in new[]
        {
            "klik koordinat 100 200",
            "klik posisi 100 200",
            "klik layar 300 400",
            "mouse click 100 200"
        })
        {
            AssistantIntent routed = router.Route(unsupported);
            Require(
                routed.Kind != AssistantIntentKind.Action,
                $"Arbitrary coordinate command became executable: {unsupported}");
        }

        AssistantIntent intent = router.Route("cari tombol Save di window Notepad");
        Require(
            intent.Kind == AssistantIntentKind.Tool &&
            intent.Tool?.Name == BuiltInToolNames.DesktopInspectUi &&
            intent.Tool.IncludeInContext == false,
            "UI inspection escaped private local tool routing.");

        var fakeUi = new FakeUiAutomationReader { Snapshot = snapshot };
        using var handler = new FakeHttp((_, _) =>
            throw new InvalidOperationException("Gemini must not be called."));
        using var client = new HttpClient(handler);
        var chat = new ChatCoordinator(
            new FakeCredentials { Key = "fake-uia-key" },
            new ChatSettings
            {
                Provider = ChatProvider.Gemini,
                UseDesktopActions = true
            },
            () => null,
            client);
        var tools = new AssistantToolRouter(new IAssistantTool[]
        {
            new InspectDesktopUiTool(() => true, catalog, fakeUi)
        });
        var assistant = new AssistantController(chat, intentRouter: router, tools: tools);

        AssistantReply reply = await assistant.SendAsync(
            new AssistantRequest("cari tombol Save di window Notepad"));
        Require(
            reply.Backend == AssistantBackend.Local && handler.Calls == 0,
            "UI control inspection reached Gemini.");
        Require(
            reply.Text.Contains("[Button] Save", StringComparison.Ordinal) &&
            fakeUi.Calls == 1,
            "UI inspection tool did not return the local resolved control.");
        Require(
            assistant.Conversation.GetRecentContext().Count == 0,
            "UI control metadata leaked into Gemini short-term context.");
        Require(reply.ActionProposal is null, "Read-only UI inspection created an action proposal.");

        AssistantIntent click = router.Route("klik tombol Refresh di window Notepad");
        Require(
            click.Kind == AssistantIntentKind.Action &&
            click.Action?.Name == BuiltInActionNames.DesktopInvokeUiControl &&
            click.Action.IncludeInContext == false,
            "UI button invoke did not route privately.");
        Require(
            router.Route("klik textbox password di window Notepad").Kind !=
                AssistantIntentKind.Action,
            "Non-button/protected UI command became executable.");

        var executor = new FakeUiActionExecutor();
        var mouse = new FakeMouseActionExecutor();
        var action = new InvokeDesktopUiControlAction(
            () => true,
            catalog,
            fakeUi,
            executor,
            mouse);
        fakeUi.Snapshot = snapshot;
        ActionPreparationResult prepared = await action.PrepareAsync(click.Action!);
        Require(
            prepared.Success && prepared.Action is not null && !prepared.Action.IncludeInContext,
            "UI button action was not prepared safely.");
        ActionExecutionResult executed = await action.ExecuteAsync(prepared.Action!);
        Require(
            executed.Success && executor.Calls == 1 && mouse.Calls == 0,
            "Confirmed UI button action did not execute.");

        fakeUi.Snapshot = snapshot;
        ActionPreparationResult stalePrepared = await action.PrepareAsync(click.Action!);
        fakeUi.Snapshot = snapshot with
        {
            Nodes = snapshot.Nodes.Select(x =>
                x.Name == "Refresh" ? x with { Name = "Different Button" } : x).ToArray()
        };
        ActionExecutionResult stale = await action.ExecuteAsync(stalePrepared.Action!);
        Require(
            !stale.Success && executor.Calls == 1 && mouse.Calls == 0,
            "Changed UI control executed after confirmation.");

        fakeUi.Snapshot = snapshot;
        var actionRouter = new AssistantActionRouter(new IAssistantAction[] { action });
        var actionAssistant = new AssistantController(
            chat,
            intentRouter: router,
            tools: tools,
            actions: actionRouter);
        AssistantReply proposal = await actionAssistant.SendAsync(
            new AssistantRequest("klik tombol Refresh di window Notepad"));
        Require(
            proposal.Backend == AssistantBackend.Local &&
            proposal.ActionProposal is not null &&
            handler.Calls == 0 &&
            executor.Calls == 1,
            "UI button confirmation gate or zero-Gemini routing failed.");
        Require(
            actionAssistant.Conversation.GetRecentContext().Count == 0,
            "UI button preparation leaked into Gemini short-term context.");

        AssistantReply confirmed = await actionAssistant.ConfirmActionAsync(
            proposal.ActionProposal!.Id);
        Require(
            confirmed.Backend == AssistantBackend.Local &&
            executor.Calls == 2 &&
            handler.Calls == 0,
            "Confirmed UI button action did not remain local or execute once.");
        Require(
            actionAssistant.Conversation.GetRecentContext().Count == 0,
            "Confirmed UI button result leaked into Gemini short-term context.");

        int invokeBeforeSave = executor.Calls;
        int mouseBeforeSave = mouse.Calls;
        AssistantReply blockedSave = await actionAssistant.SendAsync(
            new AssistantRequest("klik tombol Save di window Notepad"));
        Require(
            blockedSave.Backend == AssistantBackend.Local &&
            blockedSave.ActionProposal is null &&
            handler.Calls == 0 &&
            executor.Calls == invokeBeforeSave && mouse.Calls == mouseBeforeSave,
            "Sensitive Save action escaped local policy.");
        Require(
            actionAssistant.Conversation.GetRecentContext().Count == 0,
            "Blocked UI action leaked into Gemini context.");

        executor.Result = DesktopUiInvokeResult.Unsupported("InvokePattern unavailable.");
        int uiBefore = executor.Calls;
        int mouseBefore = mouse.Calls;
        ActionPreparationResult fallbackPrepared = await action.PrepareAsync(click.Action!);
        Require(
            fallbackPrepared.Success && fallbackPrepared.Action is not null &&
            fallbackPrepared.Action.ConfirmationText.Contains("mouse", StringComparison.OrdinalIgnoreCase),
            "Mouse fallback was not disclosed in confirmation.");
        ActionExecutionResult fallback = await action.ExecuteAsync(fallbackPrepared.Action!);
        Require(
            fallback.Success && executor.Calls == uiBefore + 1 && mouse.Calls == mouseBefore + 1,
            "Unsupported InvokePattern did not use exactly one mouse fallback after UIA.");
        Require(
            mouse.Target == (window, refresh.Path, DesktopUiNodeIdentity.Fingerprint(refresh)),
            "Mouse fallback did not receive the confirmed UIA target identity.");

        foreach (DesktopUiInvokeResult noFallback in new[]
        {
            DesktopUiInvokeResult.Rejected("Target changed."),
            DesktopUiInvokeResult.Rejected("Button disabled."),
            DesktopUiInvokeResult.Rejected("Policy blocked."),
            DesktopUiInvokeResult.Rejected("UI Automation busy."),
            DesktopUiInvokeResult.Rejected("InvokePattern unavailable."),
            DesktopUiInvokeResult.Indeterminate("Invoke timed out."),
            DesktopUiInvokeResult.Indeterminate("Provider failed during Invoke."),
            new DesktopUiInvokeResult((DesktopUiInvokeOutcome)999, "Unknown status.")
        })
        {
            executor.Result = noFallback;
            mouseBefore = mouse.Calls;
            uiBefore = executor.Calls;
            ActionPreparationResult failurePrepared = await action.PrepareAsync(click.Action!);
            ActionExecutionResult failure = await action.ExecuteAsync(failurePrepared.Action!);
            Require(
                !failure.Success && executor.Calls == uiBefore + 1 && mouse.Calls == mouseBefore,
                $"Mouse fallback escaped safe failure boundary: {noFallback.Outcome}");
        }

        executor.Result = DesktopUiInvokeResult.Unsupported("InvokePattern unavailable.");
        var noMouseAction = new InvokeDesktopUiControlAction(() => true, catalog, fakeUi, executor);
        ActionExecutionResult unavailable = await noMouseAction.ExecuteAsync(fallbackPrepared.Action!);
        Require(!unavailable.Success && mouse.Calls == mouseBefore,
            "Missing mouse executor did not fail safely.");

        mouse.Result = new(false, "Hit-test rejected.");
        ActionExecutionResult mouseRejected = await action.ExecuteAsync(fallbackPrepared.Action!);
        Require(!mouseRejected.Success && mouse.Calls == mouseBefore + 1 &&
            mouseRejected.Message.Contains("Hit-test rejected.", StringComparison.Ordinal),
            "Mouse validation failure was not preserved.");
    }
}
