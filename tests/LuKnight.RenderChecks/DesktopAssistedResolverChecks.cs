using System.Net.Http;
using System.Windows;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;
using Drawing = System.Drawing;

internal static partial class Program
{
    private sealed class FakeScreenEvidence : IDesktopUiScreenEvidenceService
    {
        public int Calls;
        public readonly Dictionary<string, DesktopUiScreenEvidenceResult> Results = new();
        public Task<DesktopUiScreenEvidenceResult> CaptureAsync(DesktopWindowTarget window,
            string controlPath, string expectedFingerprint, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Results[controlPath]);
        }
    }

    private static async Task CheckAssistedResolverAsync()
    {
        var window = new DesktopWindowTarget((nint)100, 10, "fixture", "Fixture", 0, false, true);
        var a = new DesktopUiNodeSnapshot("0/1", 1, "Button", "Refresh", "First", "Button",
            new Rect(100, 100, 80, 40), true, false, false, false);
        var b = a with { Path = "0/2", AutomationId = "Second" };
        var initial = new DesktopUiControlResolution(null, new[] { a, b });
        var screen = new FakeScreenEvidence();
        var resolver = new DesktopUiAssistedResolver(screen);
        DesktopUiScreenEvidenceResult Capture(DesktopUiNodeSnapshot node) =>
            DesktopUiScreenEvidenceResult.Captured("Captured", new DesktopUiScreenEvidence(
                node.Path, DesktopUiNodeIdentity.Fingerprint(node), node.Bounds,
                new Drawing.Rectangle(92, 92, 96, 56), "image/jpeg", new byte[] { 1, 2, 3 }, 96, 56));
        var unique = new DesktopUiControlResolution(a, new[] { a });
        Require((await resolver.ResolveAsync(window, unique)).Resolution == unique && screen.Calls == 0,
            "Unique UIA match probed screen evidence.");
        screen.Results[a.Path] = Capture(a);
        screen.Results[b.Path] = DesktopUiScreenEvidenceResult.NotMapped("Not mapped");
        DesktopUiAssistedResolution result = await resolver.ResolveAsync(window, initial);
        Require(result.Resolution.Match == a && result.UsedScreenEvidence && screen.Calls == 2,
            "Captured plus NotMapped did not select exact candidate.");
        Require(screen.Results[a.Path].Evidence!.EncodedBytes.All(x => x == 0), "Evidence bytes not cleared.");
        foreach (DesktopUiScreenEvidenceResult failure in new[]
        {
            Capture(b), DesktopUiScreenEvidenceResult.Indeterminate("Timeout"),
            DesktopUiScreenEvidenceResult.Rejected("Not mapped"),
            new DesktopUiScreenEvidenceResult((DesktopUiScreenEvidenceOutcome)999, "Unknown")
        })
        {
            screen.Results[a.Path] = Capture(a);
            screen.Results[b.Path] = failure;
            result = await resolver.ResolveAsync(window, initial);
            Require(result.Resolution.Ambiguous, $"Unsafe ambiguity reduction: {failure.Outcome}");
            Require(screen.Results[a.Path].Evidence!.EncodedBytes.All(x => x == 0), "Evidence survived unsafe result.");
        }
        screen.Results[a.Path] = DesktopUiScreenEvidenceResult.NotMapped("Not mapped");
        screen.Results[b.Path] = DesktopUiScreenEvidenceResult.NotMapped("Not mapped");
        Require((await resolver.ResolveAsync(window, initial)).Resolution == initial,
            "All NotMapped did not preserve original ambiguity.");
        int before = screen.Calls;
        var tooMany = new DesktopUiControlResolution(null,
            Enumerable.Range(0, 5).Select(i => a with { Path = $"0/{i}" }).ToArray());
        Require((await resolver.ResolveAsync(window, tooMany)).Resolution == tooMany && screen.Calls == before,
            "More than four candidates were probed.");
        foreach (DesktopUiScreenEvidenceResult invalid in new[]
        {
            Capture(a) with { Evidence = Capture(a).Evidence! with { ControlPath = "wrong" } },
            Capture(a) with { Evidence = Capture(a).Evidence! with { ControlFingerprint = "wrong" } },
            new DesktopUiScreenEvidenceResult(DesktopUiScreenEvidenceOutcome.Captured, "Missing evidence")
        })
        {
            screen.Results[a.Path] = invalid;
            Require((await resolver.ResolveAsync(window, initial)).Resolution == initial,
                "Invalid evidence identity selected a candidate.");
            Require(invalid.Evidence is null || invalid.Evidence.EncodedBytes.All(x => x == 0),
                "Mismatched evidence bytes not cleared.");
        }
        screen.Results[a.Path] = Capture(a);
        var blocked = new DesktopUiControlResolution(null, new[] { a, b with { IsEnabled = false } });
        Require((await resolver.ResolveAsync(window, blocked)).Resolution == blocked,
            "Policy rejection eliminated another candidate.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        before = screen.Calls;
        try { await resolver.ResolveAsync(window, initial, cancelled.Token); Require(false, "Cancellation ignored."); }
        catch (OperationCanceledException) { }
        Require(screen.Calls == before, "Cancelled resolver probed evidence.");

        var catalog = new FakeWindowCatalog();
        catalog.Windows.Add(window);
        var ui = new FakeUiAutomationReader { Snapshot = new DesktopUiSnapshot(window, new[] { a, b }, false) };
        var executor = new FakeUiActionExecutor();
        var mouse = new FakeMouseActionExecutor();
        var action = new InvokeDesktopUiControlAction(() => true, catalog, ui, executor, mouse, resolver);
        using var handler = new FakeHttp((_, _) => throw new InvalidOperationException("Screen assist reached Gemini."));
        using var client = new HttpClient(handler);
        var chat = new ChatCoordinator(new FakeCredentials { Key = "unused-screen-key" },
            new ChatSettings { Provider = ChatProvider.Gemini, UseDesktopActions = true }, () => null, client);
        var router = new AssistantIntentRouter(new LocalDesktopCommandRouter(
            new DesktopAppCatalogService(() => Array.Empty<DesktopAppTarget>()), catalog));
        var assistant = new AssistantController(chat, intentRouter: router,
            actions: new AssistantActionRouter(new IAssistantAction[] { action }));
        screen.Results[a.Path] = Capture(a);
        AssistantReply proposal = await assistant.SendAsync(new AssistantRequest("klik tombol Refresh di window Fixture"));
        Require(proposal.ActionProposal is not null && executor.Calls == 0 && mouse.Calls == 0 && handler.Calls == 0 &&
            assistant.Conversation.GetRecentContext().Count == 0, "Assisted proposal escaped confirmation/privacy.");
        Require(proposal.ActionProposal!.ConfirmationText.Contains("validasi layar lokal", StringComparison.Ordinal),
            "Screen assistance not disclosed.");
        await assistant.ConfirmActionAsync(proposal.ActionProposal.Id);
        Require(executor.Calls == 1 && mouse.Calls == 0 && handler.Calls == 0 &&
            assistant.Conversation.GetRecentContext().Count == 0, "Assisted confirmation bypassed ordinary pipeline/privacy.");
        ui.Snapshot = new DesktopUiSnapshot(window, new[] { a }, false);
        before = screen.Calls;
        await action.PrepareAsync(router.Route("klik tombol Refresh di window Fixture").Action!);
        Require(screen.Calls == before, "Unique button preparation used screen assist.");
    }
}
