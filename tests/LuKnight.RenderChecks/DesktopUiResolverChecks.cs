using System.Net.Http;
using System.Windows;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
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
    }
}
