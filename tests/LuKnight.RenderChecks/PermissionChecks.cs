using System.Net.Http;
using System.Text.Json;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private sealed class PermissionAction : IAssistantAction
    {
        public string Name => BuiltInActionNames.DesktopInvokeUiControl;
        public AssistantActionRisk Risk;
        public int Executions;
        public ActionPreparationResult Prepare(ActionInvocation invocation) =>
            new(true, "Prepared.", new PreparedAssistantAction(Name,
                new Dictionary<string, string> { ["value"] = "private sentinel" },
                "Permission Test", "Confirm permission test?", IncludeInContext: false, Risk: Risk));
        public Task<ActionExecutionResult> ExecuteAsync(PreparedAssistantAction action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Executions++;
            return Task.FromResult(new ActionExecutionResult(true, "Executed."));
        }
    }

    private static async Task CheckPermissionLevelsAsync()
    {
        DesktopPermissionLevel current = DesktopPermissionLevel.Interaction;
        var action = new PermissionAction();
        var router = new AssistantActionRouter(new[] { action }, () => current);
        var invocation = new ActionInvocation(action.Name, new Dictionary<string, string>(), IncludeInContext: false);
        foreach (DesktopPermissionLevel level in Enum.GetValues<DesktopPermissionLevel>())
        foreach (AssistantActionRisk risk in Enum.GetValues<AssistantActionRisk>())
        {
            current = level;
            action.Risk = risk;
            bool expected = risk != AssistantActionRisk.Prohibited && (int)level > (int)risk;
            var sync = router.Prepare(invocation);
            var prepared = await router.PrepareAsync(invocation);
            Require(sync.Success == expected && prepared.Success == expected,
                $"Permission matrix failed: {level}/{risk}");
            Require(prepared.Action is not null && !prepared.Action.IncludeInContext,
                "Permission gate lost private metadata.");
            Require(!prepared.Message.Contains("private sentinel", StringComparison.Ordinal), "Permission policy leaked arguments.");
            int before = action.Executions;
            var result = await router.ExecuteAsync(prepared.Action!);
            Require(result.Success == expected && action.Executions == before + (expected ? 1 : 0),
                "Execution permission gate failed.");
        }
        current = (DesktopPermissionLevel)999;
        action.Risk = AssistantActionRisk.Navigation;
        Require(!(await router.PrepareAsync(invocation)).Success, "Unknown permission allowed.");
        current = DesktopPermissionLevel.Sensitive;
        action.Risk = (AssistantActionRisk)999;
        Require(!router.Prepare(invocation).Success, "Unknown risk allowed.");
        Require(new PreparedAssistantAction("test", new Dictionary<string,string>(), "Test", "Confirm").Risk ==
            AssistantActionRisk.Interaction, "Default action risk changed.");
        var legacy = JsonSerializer.Deserialize<AppSettings>("{\"SchemaVersion\":1,\"Chat\":{\"UseDesktopActions\":true}}")!;
        Require(legacy.Chat.DesktopPermission == DesktopPermissionLevel.Interaction && legacy.SchemaVersion == 1,
            "Legacy settings lost default permission.");
        Require(!new ChatSettings().UseDesktopActions, "New permission enabled desktop actions by default.");
        try
        {
            SettingsService.Validate(new AppSettings { Chat = new ChatSettings { DesktopPermission = (DesktopPermissionLevel)999 } });
            Require(false, "Invalid settings permission accepted.");
        }
        catch (ArgumentException) { }

        using var handler = new FakeHttp((_, _) => throw new InvalidOperationException("Permission test reached Gemini."));
        using var client = new HttpClient(handler);
        var chat = new ChatCoordinator(new FakeCredentials { Key = "unused-permission-key" },
            new ChatSettings { Provider = ChatProvider.Gemini, UseDesktopActions = true }, () => null, client);
        var assistant = new AssistantController(chat, actions: router);
        action.Risk = AssistantActionRisk.Interaction;
        current = DesktopPermissionLevel.Interaction;
        int executionsBefore = action.Executions;
        AssistantReply proposal = await assistant.SendAsync(new AssistantRequest("klik tombol Refresh di window Fixture"));
        Require(proposal.ActionProposal?.Risk == AssistantActionRisk.Interaction && action.Executions == executionsBefore,
            "Allowed action auto-executed or proposal lost risk.");
        current = DesktopPermissionLevel.ObserveOnly;
        AssistantReply confirmed = await assistant.ConfirmActionAsync(proposal.ActionProposal!.Id);
        Require(action.Executions == executionsBefore && confirmed.Backend == AssistantBackend.Local,
            "Permission downgrade failed to block confirmation.");
        var blocked = await assistant.SendAsync(new AssistantRequest("klik tombol Refresh di window Fixture"));
        Require(blocked.ActionProposal is null && handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
            "Permission rejection exposed private action/context.");
        current = DesktopPermissionLevel.Sensitive;
        action.Risk = AssistantActionRisk.Navigation;
        proposal = await assistant.SendAsync(new AssistantRequest("klik tombol Refresh di window Fixture"));
        Require(proposal.ActionProposal?.Risk == AssistantActionRisk.Navigation && action.Executions == executionsBefore,
            "Navigation auto-executed at Sensitive permission.");
        await assistant.ConfirmActionAsync(proposal.ActionProposal!.Id);
        Require(action.Executions == executionsBefore + 1 && handler.Calls == 0,
            "Allowed confirmed action did not execute locally exactly once.");
    }
}
