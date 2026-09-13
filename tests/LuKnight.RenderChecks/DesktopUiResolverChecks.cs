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

        public Task<DesktopActionResult> InvokeAsync(
            DesktopWindowTarget window,
            string controlPath,
            string expectedFingerprint,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(new DesktopActionResult(true, "Button invoked."));
        }
    }

    private static async Task CheckDesktopUiResolverAsync()
    {
        DesktopWindowTarget window = new(
            (nint)100, 10, "notepad", "Notes", 0, false, true);
        DesktopUiSnapshot snapshot = new(
            window,
            new DesktopUiNodeSnapshot[]
            {
                new("0/0", 1, "Button", "Save", "SaveButton", "Button", Rect.Empty, true, false, false, false),
                new("0/1", 1, "Button", "Save As", "SaveAsButton", "Button", Rect.Empty, true, false, false, false),
                new("0/2", 1, "Edit", "Search", "SearchBox", "Edit", Rect.Empty, true, false, true, false),
                new("0/3", 1, "Edit", "[protected]", "", "", Rect.Empty, true, false, false, true)
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
            buttons.Count == 2 && buttons.All(x => x.ControlType == "Button"),
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

        AssistantIntent click = router.Route("klik tombol Save di window Notepad");
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
        var action = new InvokeDesktopUiControlAction(
            () => true,
            catalog,
            fakeUi,
            executor);
        fakeUi.Snapshot = snapshot;
        ActionPreparationResult prepared = await action.PrepareAsync(click.Action!);
        Require(
            prepared.Success && prepared.Action is not null && !prepared.Action.IncludeInContext,
            "UI button action was not prepared safely.");
        ActionExecutionResult executed = await action.ExecuteAsync(prepared.Action!);
        Require(
            executed.Success && executor.Calls == 1,
            "Confirmed UI button action did not execute.");

        fakeUi.Snapshot = snapshot;
        ActionPreparationResult stalePrepared = await action.PrepareAsync(click.Action!);
        fakeUi.Snapshot = snapshot with
        {
            Nodes = snapshot.Nodes.Select(x =>
                x.Name == "Save" ? x with { Name = "Different Button" } : x).ToArray()
        };
        ActionExecutionResult stale = await action.ExecuteAsync(stalePrepared.Action!);
        Require(
            !stale.Success && executor.Calls == 1,
            "Changed UI control executed after confirmation.");

        fakeUi.Snapshot = snapshot;
        var actionRouter = new AssistantActionRouter(new IAssistantAction[] { action });
        var actionAssistant = new AssistantController(
            chat,
            intentRouter: router,
            tools: tools,
            actions: actionRouter);
        AssistantReply proposal = await actionAssistant.SendAsync(
            new AssistantRequest("klik tombol Save di window Notepad"));
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
    }
}
