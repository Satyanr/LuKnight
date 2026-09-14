using System.Net.Http;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private sealed class MutablePlanAppCatalog
        : IDesktopAppCatalog
    {
        public List<DesktopAppTarget> Items { get; } =
            new();

        public IReadOnlyList<DesktopAppTarget>
            Applications =>
            Items;

        public void Add(
            DesktopAppTarget target) =>
            Items.Add(
                target);

        public DesktopAppResolution Resolve(
            string query)
        {
            DesktopAppTarget[] matches =
                Items.Where(
                        app =>
                            string.Equals(
                                app.DisplayName,
                                query,
                                StringComparison.OrdinalIgnoreCase) ||
                            app.Aliases.Any(
                                alias =>
                                    string.Equals(
                                        alias,
                                        query,
                                        StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

            return matches.Length switch
            {
                1 =>
                    new(
                        matches[0],
                        matches,
                        false),

                > 1 =>
                    new(
                        null,
                        matches,
                        true),

                _ =>
                    new(
                        null,
                        [],
                        false)
            };
        }

        public bool TryResolveById(
            string id,
            out DesktopAppTarget target)
        {
            target =
                Items.FirstOrDefault(
                    app =>
                        string.Equals(
                            app.Id,
                            id,
                            StringComparison.OrdinalIgnoreCase))!;

            return target is not null;
        }

        public void Refresh()
        {
        }
    }


    private sealed class PlanTestAction(string name) : IAssistantAction
    {
        public string Name => name;
        public int Preparations, Executions;
        public AssistantActionRisk Risk = AssistantActionRisk.Navigation;
        public bool Fail, Throw, FailPrepare, ThrowPrepare;
        public Action? OnExecute;
        public ActionPreparationResult Prepare(ActionInvocation invocation)
        {
            Preparations++;
            if (ThrowPrepare) throw new OperationCanceledException("Prepare cancelled");
            return new(!FailPrepare, "Preparation result", new(Name, invocation.Arguments, "Test", "Confirm?", Risk: Risk));
        }
        public Task<ActionExecutionResult> ExecuteAsync(PreparedAssistantAction action, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Throw) throw new OperationCanceledException("Test cancellation");
            Executions++;
            OnExecute?.Invoke();
            return Task.FromResult(new ActionExecutionResult(!Fail, "Execution result"));
        }
    }

    private static async Task CheckPlannerExecutionAsync()
    {
        static DesktopAppTarget App(string id, string name) => new(id, name, id, new[] { id }, new[] { name.ToLowerInvariant() }, DesktopAppSource.BuiltIn);
        foreach (string scenario in new[] { "success", "restricted", "conversation", "cancel", "failure", "exception", "prepare-failure", "downgrade", "sensitive", "sensitive-downgrade", "cancel-token", "prepare-exception" })
        {
            var catalog = new MutablePlanAppCatalog();
            catalog.Add(App("launcher", "Launcher"));
            var open = new PlanTestAction(BuiltInActionNames.DesktopOpenApplication);
            var focus = new PlanTestAction(BuiltInActionNames.DesktopFocusApplication);
            open.OnExecute = () => catalog.Add(App("target", "Target"));
            open.Fail = scenario == "failure";
            open.Throw = scenario == "exception";
            focus.FailPrepare = scenario == "prepare-failure";
            focus.ThrowPrepare = scenario == "prepare-exception";
            if (scenario.StartsWith("sensitive", StringComparison.Ordinal)) open.Risk = AssistantActionRisk.Sensitive;
            DesktopPermissionLevel level = DesktopPermissionLevel.Sensitive;
            using var handler = new FakeHttp((_, _) => throw new InvalidOperationException("Plan called Gemini."));
            using var client = new HttpClient(handler);
            var chat = new ChatCoordinator(new FakeCredentials { Key = "unused-plan-key" },
                new ChatSettings { Provider = ChatProvider.Gemini, UseDesktopActions = true }, () => null, client);
            var assistant = new AssistantController(chat,
                intentRouter: new AssistantIntentRouter(new LocalDesktopCommandRouter(catalog)),
                actions: new AssistantActionRouter(new IAssistantAction[] { open, focus }, () => level));
            string next = scenario switch { "restricted" => "jalankan powershell", "conversation" => "ceritakan lelucon", _ => "fokus target" };
            var first = await assistant.SendAsync(new AssistantRequest("buka launcher lalu " + next));
            Require(first.ActionProposal is not null && open.Executions == 0 && focus.Preparations == 0 && !catalog.Resolve("target").Found,
                "Future step resolved early: " + scenario);
            Require(assistant.HasPendingAction && assistant.HasPendingPlan && assistant.IsBusy, "Pending state missing.");
            try { assistant.ClearConversation(); Require(false, "Cleared pending plan."); } catch (InvalidOperationException) { }
            try { await assistant.SendAsync(new AssistantRequest("hello")); Require(false, "Overwrote pending plan."); } catch (InvalidOperationException) { }
            try { await assistant.ConfirmActionAsync(Guid.NewGuid()); Require(false, "Invalid id accepted."); } catch (InvalidOperationException) { }
            Require(assistant.HasPendingPlan && assistant.HasPendingAction, "Invalid id destroyed plan.");
            if (scenario == "cancel") assistant.CancelAction(first.ActionProposal!.Id);
            else if (scenario is "exception" or "cancel-token" or "prepare-exception")
            {
                try { await assistant.ConfirmActionAsync(first.ActionProposal!.Id, scenario == "cancel-token" ? new CancellationToken(true) : default); Require(false, "Cancellation did not throw."); }
                catch (OperationCanceledException) { }
            }
            else
            {
                if (scenario == "downgrade") level = DesktopPermissionLevel.ObserveOnly;
                var second = await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
                if (scenario.StartsWith("sensitive", StringComparison.Ordinal))
                {
                    Require(second.ActionProposal?.ConfirmationStage == AssistantConfirmationStage.SensitiveFinal && open.Executions == 0 && focus.Preparations == 0 && assistant.HasPendingPlan,
                        "Sensitive review advanced plan.");
                    if (scenario == "sensitive-downgrade") level = DesktopPermissionLevel.ObserveOnly;
                    second = await assistant.ConfirmActionAsync(second.ActionProposal!.Id);
                }
                if (scenario is "success" or "sensitive")
                {
                    Require(open.Executions == 1 && catalog.Resolve("target").Found && focus.Preparations == 1 && focus.Executions == 0 && second.ActionProposal is not null,
                        "Fresh routing failed.");
                    var finished = await assistant.ConfirmActionAsync(second.ActionProposal!.Id);
                    Require(focus.Executions == 1 && finished.ActionProposal is null, "Final step failed.");
                }
                else
                {
                    Require(second.ActionProposal is null && focus.Executions == 0, "Failed step continued.");
                    if (scenario == "restricted") Require(second.Text.Contains("tidak diizinkan", StringComparison.OrdinalIgnoreCase), "Restricted reason lost.");
                }
            }
            if (scenario is "cancel" or "cancel-token" or "exception" or "downgrade" or "sensitive-downgrade")
                Require(open.Executions == 0 && focus.Executions == 0, "Blocked plan executed an action.");
            Require(!assistant.HasPendingPlan && !assistant.HasPendingAction && !assistant.IsBusy, "Plan state not cleared: " + scenario);
            Require(handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0, "Plan leaked Gemini/context.");
        }
    }
}
