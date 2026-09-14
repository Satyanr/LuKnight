using System.Net.Http;
using System.Windows;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private static async Task CheckDesktopUiRiskAsync()
    {
        var window = new DesktopWindowTarget((nint)100, 10, "Editor", "Document", 0, false, true);
        var button = new DesktopUiNodeSnapshot("0/1", 1, "Button", "Refresh", "RefreshButton", "Button",
            new Rect(100, 100, 80, 40), true, false, false, false);
        void Check(DesktopUiNodeSnapshot node, AssistantActionRisk expected, DesktopWindowTarget? context = null)
        {
            var assessment = DesktopUiActionRiskClassifier.ClassifyButton(node, context ?? window);
            Require(assessment.Valid && assessment.Risk == expected, $"Unexpected risk for {node.Name}: {assessment.Risk}");
        }
        foreach (string label in new[] { "Refresh", "Back", "Search", "Shipping", "Tokenizer", "Pinpoint", "Address2", "Refresh1" })
            Check(button with { Name = label }, AssistantActionRisk.Interaction);
        foreach (string label in new[]
        {
            "Save", "Save As...", "Delete", "Send", "Upload", "Pay", "Checkout", "Install", "Update",
            "Shutdown", "Login", "Jalankan", "Hapus", "Kirim", "Bayar", "&Save", "_Delete", "Delete2"
        })
            Check(button with { Name = label }, AssistantActionRisk.Sensitive);
        foreach (string label in new[] { "OK", "Yes", "Confirm", "Continue", "Apply", "Lanjut", "Terapkan" })
            Check(button with { Name = label }, AssistantActionRisk.Prohibited);
        foreach (string id in new[] { "Delete2", "PayButton", "SaveAs2", "UploadButton" })
            Check(button with { Name = "Action", AutomationId = id }, AssistantActionRisk.Sensitive);
        Check(button with { ClassName = "DeleteButton" }, AssistantActionRisk.Sensitive);
        Check(button with { Name = "Next" }, AssistantActionRisk.Sensitive, window with { Title = "Checkout — Example Store" });
        Check(button, AssistantActionRisk.Sensitive, window with { ProcessName = "installerUpdate" });
        foreach (DesktopUiNodeSnapshot invalid in new[]
        {
            button with { IsPassword = true }, button with { IsEnabled = false }, button with { IsOffscreen = true },
            button with { ControlType = "Edit" }, button with { Name = "" }
        })
        {
            var assessment = DesktopUiActionRiskClassifier.ClassifyButton(invalid, window);
            Require(!assessment.Valid && assessment.Risk == AssistantActionRisk.Prohibited,
                "Structurally invalid control was allowed.");
        }
        var catalog = new FakeWindowCatalog();
        catalog.Windows.Add(window);
        var ui = new FakeUiAutomationReader { Snapshot = new DesktopUiSnapshot(window, new[] { button }, false) };
        var executor = new FakeUiActionExecutor();
        var mouse = new FakeMouseActionExecutor();
        var action = new InvokeDesktopUiControlAction(() => true, catalog, ui, executor, mouse);
        using var handler = new FakeHttp((_, _) => throw new InvalidOperationException("Risk classification reached Gemini."));
        using var client = new HttpClient(handler);
        var chat = new ChatCoordinator(new FakeCredentials { Key = "unused-risk-key" }, new ChatSettings
        {
            Provider = ChatProvider.Gemini, UseDesktopActions = true, DesktopPermission = DesktopPermissionLevel.Sensitive
        }, () => null, client);
        var intentRouter = new AssistantIntentRouter(new LocalDesktopCommandRouter(
            new DesktopAppCatalogService(() => Array.Empty<DesktopAppTarget>()), catalog));
        var assistant = new AssistantController(chat, intentRouter: intentRouter,
            actions: new AssistantActionRouter(new[] { action }, () => chat.Options.DesktopPermission));
        foreach (string label in new[] { "Save", "Delete", "Pay", "Send", "OK" })
        {
            ui.Snapshot = new DesktopUiSnapshot(window, new[] { button with { Name = label } }, false);
            var reply = await assistant.SendAsync(new AssistantRequest($"klik tombol {label} di window Editor"));
            Require(reply.ActionProposal is null && executor.Calls == 0 && mouse.Calls == 0 && handler.Calls == 0 &&
                assistant.Conversation.GetRecentContext().Count == 0,
                "Sensitive UI action became executable before stronger confirmation or leaked context.");
        }
        ui.Snapshot = new DesktopUiSnapshot(window, new[] { button }, false);
        var invocation = intentRouter.Route("klik tombol Refresh di window Editor").Action!;
        var prepared = await action.PrepareAsync(invocation);
        Require(prepared.Action?.Risk == AssistantActionRisk.Interaction, "Prepared risk did not reflect classification.");
        var changedRisk = await action.ExecuteAsync(prepared.Action! with { Risk = AssistantActionRisk.Navigation });
        Require(!changedRisk.Success && executor.Calls == 0 && mouse.Calls == 0,
            "Execution did not reclassify and compare risk.");
        ui.Snapshot = new DesktopUiSnapshot(window, new[] { button with { AutomationId = "PayButton" } }, false);
        var stale = await action.ExecuteAsync(prepared.Action!);
        Require(!stale.Success && executor.Calls == 0, "Changed sensitive metadata reached native executor.");
        ui.Snapshot = new DesktopUiSnapshot(window, new[] { button }, false);
        var proposal = await assistant.SendAsync(new AssistantRequest("klik tombol Refresh di window Editor"));
        Require(proposal.ActionProposal?.Risk == AssistantActionRisk.Interaction && executor.Calls == 0,
            "Normal interaction lost confirmation or risk metadata.");
        await assistant.ConfirmActionAsync(proposal.ActionProposal!.Id);
        Require(executor.Calls == 1 && mouse.Calls == 0 && handler.Calls == 0,
            "Normal confirmed interaction no longer executes locally.");
    }
}
