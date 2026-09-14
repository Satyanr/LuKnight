using System.Net.Http;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private static async Task CheckStrongConfirmationAsync()
    {
        DesktopPermissionLevel level = DesktopPermissionLevel.Sensitive;
        var action = new PermissionAction { Risk = AssistantActionRisk.Sensitive };
        var router = new AssistantActionRouter(new[] { action }, () => level);
        using var handler = new FakeHttp((_, _) => throw new InvalidOperationException("Strong confirmation reached Gemini."));
        using var client = new HttpClient(handler);
        var chat = new ChatCoordinator(new FakeCredentials { Key = "unused-strong-key" },
            new ChatSettings { Provider = ChatProvider.Gemini, UseDesktopActions = true }, () => null, client);
        var assistant = new AssistantController(chat, actions: router);
        Task<AssistantReply> Propose() => assistant.SendAsync(new AssistantRequest("klik tombol Save di window Fixture"));
        async Task Replay(Guid id)
        {
            try { await assistant.ConfirmActionAsync(id); Require(false, "Replayed confirmation accepted."); }
            catch (InvalidOperationException) { }
        }
        var first = await Propose();
        Require(first.ActionProposal is { ConfirmationStage: AssistantConfirmationStage.SensitiveReview } && action.Executions == 0,
            "Sensitive action did not start at review.");
        var final = await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
        Require(final.ActionProposal is { ConfirmationStage: AssistantConfirmationStage.SensitiveFinal } && action.Executions == 0,
            "First Yes reached executor.");
        Require(final.ActionProposal!.Id != first.ActionProposal.Id &&
            final.ActionProposal.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30), "Final id/expiry invalid.");
        await Replay(first.ActionProposal.Id);
        await assistant.ConfirmActionAsync(final.ActionProposal.Id);
        Require(action.Executions == 1 && !assistant.HasPendingAction, "Two-stage confirmation did not execute once.");
        await Replay(final.ActionProposal.Id);
        foreach (bool downgradeBeforeReview in new[] { true, false })
        {
            level = DesktopPermissionLevel.Sensitive;
            first = await Propose();
            if (!downgradeBeforeReview) first = await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
            level = DesktopPermissionLevel.ObserveOnly;
            var blocked = await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
            Require(action.Executions == 1 && blocked.ActionProposal is null && !assistant.HasPendingAction,
                "Permission downgrade allowed strong confirmation.");
        }
        level = DesktopPermissionLevel.Sensitive;
        first = await Propose();
        final = await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
        assistant.CancelAction(final.ActionProposal!.Id);
        Require(action.Executions == 1 && !assistant.HasPendingAction, "Cancelled final executed.");
        await Replay(final.ActionProposal.Id);
        action.Risk = AssistantActionRisk.Prohibited;
        Require((await Propose()).ActionProposal is null, "Prohibited risk got proposal.");
        action.Risk = AssistantActionRisk.Sensitive;
        level = DesktopPermissionLevel.Interaction;
        Require((await Propose()).ActionProposal is null, "Sensitive risk allowed at Interaction.");
        action.Risk = AssistantActionRisk.Interaction;
        first = await Propose();
        Require(first.ActionProposal?.ConfirmationStage == AssistantConfirmationStage.Standard, "Interaction stage changed.");
        await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
        Require(action.Executions == 2, "Interaction did not execute after one Yes.");
        level = DesktopPermissionLevel.Sensitive;
        foreach (AssistantActionRisk risk in Enum.GetValues<AssistantActionRisk>())
        foreach (AssistantActionConfirmation confirmation in Enum.GetValues<AssistantActionConfirmation>().Append((AssistantActionConfirmation)999))
        {
            var prepared = new PreparedAssistantAction(action.Name, new Dictionary<string,string>(), "Test", "Confirm",
                Risk: risk, Confirmation: confirmation);
            bool expected = risk switch
            {
                AssistantActionRisk.Navigation or AssistantActionRisk.Interaction => confirmation is AssistantActionConfirmation.Standard or AssistantActionConfirmation.Strong,
                AssistantActionRisk.Sensitive => confirmation == AssistantActionConfirmation.Strong,
                _ => false
            };
            int before = action.Executions;
            var result = await router.ExecuteAsync(prepared);
            Require(result.Success == expected && action.Executions == before + (expected ? 1 : 0),
                $"Confirmation matrix failed: {risk}/{confirmation}");
        }
        Require(handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
            "Strong confirmation leaked Gemini/context.");

        var window = new DesktopWindowTarget((nint)100, 10, "Editor", "Document", 0, false, true);
        var button = new DesktopUiNodeSnapshot("0/1", 1, "Button", "Save", "SaveButton", "Button",
            new System.Windows.Rect(10, 10, 80, 40), true, false, false, false);
        var catalog = new FakeWindowCatalog(); catalog.Windows.Add(window);
        var ui = new FakeUiAutomationReader { Snapshot = new DesktopUiSnapshot(window, new[] { button }, false) };
        var native = new FakeUiActionExecutor();
        var mouse = new FakeMouseActionExecutor();
        var buttonAction = new InvokeDesktopUiControlAction(() => true, catalog, ui, native, mouse);
        var intentRouter = new AssistantIntentRouter(new LocalDesktopCommandRouter(
            new DesktopAppCatalogService(() => Array.Empty<DesktopAppTarget>()), catalog));
        var buttonAssistant = new AssistantController(chat, intentRouter: intentRouter,
            actions: new AssistantActionRouter(new[] { buttonAction }, () => DesktopPermissionLevel.Sensitive));
        first = await buttonAssistant.SendAsync(new AssistantRequest("klik tombol Save di window Editor"));
        Require(first.ActionProposal?.ConfirmationStage == AssistantConfirmationStage.SensitiveReview && native.Calls == 0,
            "Save skipped sensitive review.");
        final = await buttonAssistant.ConfirmActionAsync(first.ActionProposal!.Id);
        Require(native.Calls == 0 && mouse.Calls == 0 && final.ActionProposal?.ConfirmationStage == AssistantConfirmationStage.SensitiveFinal,
            "Save executed after first Yes.");
        await buttonAssistant.ConfirmActionAsync(final.ActionProposal!.Id);
        Require(native.Calls == 1 && mouse.Calls == 0 && handler.Calls == 0 &&
            buttonAssistant.Conversation.GetRecentContext().Count == 0, "Save strong confirmation failed.");
    }
}
