using LuKnight.Assistant;
using LuKnight.Services;

internal static partial class Program
{
    private sealed class FakeWindowCatalog : IDesktopWindowTargetCatalog
    {
        public List<DesktopWindowTarget> Windows { get; } = new();

        public IReadOnlyList<DesktopWindowTarget> Capture() => Windows.ToArray();

        public DesktopWindowResolution Resolve(string query) =>
            DesktopWindowTargetService.ResolveSnapshot(Windows, query);

        public bool TryResolveById(string id, out DesktopWindowTarget target)
        {
            target = Windows.FirstOrDefault(x => x.Id == id)!;
            return target is not null;
        }
    }

    private sealed class FakeWindowActionExecutor : IDesktopWindowActionExecutor
    {
        public int FocusCalls;
        public DesktopWindowTarget? LastTarget;

        public DesktopActionResult Focus(DesktopWindowTarget target)
        {
            FocusCalls++;
            LastTarget = target;
            return new(true, "Window difokuskan.");
        }
    }

    private static async Task CheckWindowTargetingAsync()
    {
        var catalog = new FakeWindowCatalog();
        catalog.Windows.AddRange(
        [
            new DesktopWindowTarget((nint)101, 10, "chrome", "Gmail - Work", 0, false, true),
            new DesktopWindowTarget((nint)102, 10, "chrome", "YouTube", 1, false, false),
            new DesktopWindowTarget((nint)201, 20, "code", "LuKnight - Visual Studio Code", 2, false, false)
        ]);

        var app = Installed("Chrome", "chrome");
        var apps = new DesktopAppCatalogService(() => [app]);
        var router = new LocalDesktopCommandRouter(apps, catalog);

        Require(
            router.TryRoute("fokus window Gmail")?.Kind == AssistantIntentKind.Action &&
            router.TryRoute("fokus window Gmail")?.Action?.Name == BuiltInActionNames.DesktopFocusWindow,
            "Exact window title did not route to action.");
        Require(
            router.TryRoute("pindah ke window LuKnight")?.Kind == AssistantIntentKind.Action,
            "Specific VS Code window was not resolved.");
        Require(
            router.TryRoute("fokus window chrome")?.Kind == AssistantIntentKind.LocalResponse,
            "Ambiguous windows were silently selected.");
        Require(
            router.TryRoute("fokus chrome")?.Action?.Name == BuiltInActionNames.DesktopFocusApplication,
            "Existing app-level focus behavior regressed.");

        DesktopWindowResolution active =
            DesktopWindowTargetService.ResolveSnapshot(catalog.Windows, "window aktif");
        Require(active.Match?.Title == "Gmail - Work", "Foreground window was not resolved.");

        DesktopWindowTarget[] noExternalForeground = catalog.Windows
            .Select(x => x with { IsForeground = false })
            .ToArray();
        DesktopWindowResolution fallback =
            DesktopWindowTargetService.ResolveSnapshot(noExternalForeground, "window aktif");
        Require(fallback.Match?.Title == "Gmail - Work",
            "Current external window was lost when Lu-Knight owned foreground.");

        DesktopWindowTarget[] minimizedTop =
        [
            catalog.Windows[0] with { IsForeground = false, IsMinimized = true },
            catalog.Windows[1] with { IsForeground = false }
        ];
        DesktopWindowResolution visibleFallback =
            DesktopWindowTargetService.ResolveSnapshot(minimizedTop, "window aktif");
        Require(visibleFallback.Match?.Title == "YouTube",
            "Active fallback selected a minimized window over a visible window.");

        Require(DesktopWindowTargetService.ResolveSnapshot(catalog.Windows, "chrome").Ambiguous,
            "Production resolver silently chose one of several Chrome windows.");

        var executor = new FakeWindowActionExecutor();
        var action = new FocusDesktopWindowAction(() => true, catalog, executor);
        AssistantIntent exact = router.TryRoute("fokus window Gmail")!;
        ActionPreparationResult prepared = action.Prepare(exact.Action!);
        Require(prepared.Success && prepared.Action is not null && !prepared.Action.IncludeInContext,
            "Exact window action could not be prepared privately.");

        AssistantIntent ambiguous = router.TryRoute("fokus window chrome")!;
        Require(ambiguous.Kind == AssistantIntentKind.LocalResponse && !ambiguous.IncludeLocalResponseInContext,
            "Window ambiguity details were allowed into Gemini context.");

        DesktopWindowTarget original = catalog.Windows[0];
        catalog.Windows[0] = original with { Title = "Different Gmail Window" };
        ActionExecutionResult changed = await action.ExecuteAsync(prepared.Action!);
        Require(!changed.Success && executor.FocusCalls == 0,
            "Changed window target executed after confirmation.");

        catalog.Windows[0] = original;
        ActionExecutionResult confirmed = await action.ExecuteAsync(prepared.Action!);
        Require(confirmed.Success && executor.FocusCalls == 1 && executor.LastTarget?.Id == original.Id,
            "Unchanged window target did not execute after confirmation.");

        var missing = router.TryRoute("fokus window missing");
        Require(missing?.Kind == AssistantIntentKind.LocalResponse,
            "Missing window target did not remain local.");

        var conversation = new ConversationManager();
        conversation.AddAssistant("chrome — Gmail - Work", includeInContext: false);
        conversation.AddAssistant("Pesan aman.");
        IReadOnlyList<ConversationTurn> context = conversation.GetRecentContext();
        Require(context.Count == 1 && context[0].Text == "Pesan aman.",
            "Private local window title leaked into assistant context.");

        Console.WriteLine("Window targeting checks: exact title, ambiguity, app-level compatibility, fingerprint mutation, and local routing passed.");
    }
}
