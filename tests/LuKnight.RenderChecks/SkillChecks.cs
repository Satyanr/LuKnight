using System.Net.Http;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private sealed class TestWorkflowSkill : IAssistantSkill
    {
        public string Id => "test-workflow";
        public string DisplayName => "Test Workflow";
        public IReadOnlyList<string> Aliases { get; } = ["test-flow"];

        public SkillExpansionResult Expand(SkillInvocation invocation) =>
            SkillExpansionResult.Expanded(
                new AssistantPlan(
                    new AssistantPlanStep[]
                    {
                        new(0, "buka launcher"),
                        new(1, "fokus launcher")
                    }));
    }

    private sealed class InvalidPlanSkill(
        string id,
        IReadOnlyList<AssistantPlanStep> steps) : IAssistantSkill
    {
        public string Id => id;
        public string DisplayName => id;
        public IReadOnlyList<string> Aliases => [];
        public SkillExpansionResult Expand(SkillInvocation invocation) =>
            SkillExpansionResult.Expanded(new AssistantPlan(steps));
    }

    private sealed class RestrictedWorkflowSkill : IAssistantSkill
    {
        public string Id => "restricted-workflow";
        public string DisplayName => "Restricted Workflow";
        public IReadOnlyList<string> Aliases => [];
        public SkillExpansionResult Expand(SkillInvocation invocation) =>
            SkillExpansionResult.Expanded(
                new AssistantPlan(new[]
                {
                    new AssistantPlanStep(0, "jalankan powershell")
                }));
    }

    private static async Task CheckSkillsAsync()
    {
        CheckSkillParserAndRegistry();

        var catalog = new MutablePlanAppCatalog();
        catalog.Add(new DesktopAppTarget(
            "launcher", "Launcher", "launcher", ["launcher"], ["launcher"],
            DesktopAppSource.BuiltIn));
        var open = new PlanTestAction(BuiltInActionNames.DesktopOpenApplication);
        var focus = new PlanTestAction(BuiltInActionNames.DesktopFocusApplication);
        using var handler = new FakeHttp((_, _) =>
            throw new InvalidOperationException("Local skill called Gemini."));
        using var client = new HttpClient(handler);
        var chat = new ChatCoordinator(
            new FakeCredentials { Key = "unused-skill-key" },
            new ChatSettings
            {
                Provider = ChatProvider.Gemini,
                UseDesktopActions = true,
                DesktopPermission = DesktopPermissionLevel.Sensitive
            },
            () => null,
            client);
        var assistant = new AssistantController(
            chat,
            intentRouter: new AssistantIntentRouter(new LocalDesktopCommandRouter(catalog)),
            actions: new AssistantActionRouter(
                new IAssistantAction[] { open, focus },
                () => DesktopPermissionLevel.Sensitive),
            skills: new AssistantSkillRouter(new IAssistantSkill[]
            {
                new TestWorkflowSkill()
            }));

        AssistantReply first = await assistant.SendAsync(
            new AssistantRequest("jalankan skill test-flow"));
        Require(first.ActionProposal is
        {
            IsPlanStep: true,
            PlanStepNumber: 1,
            PlanStepCount: 2
        }, "Skill did not enter planner step 1/2.");
        Require(open.Preparations == 1 && open.Executions == 0 && focus.Preparations == 0,
            "Skill resolved or executed a future step early.");

        AssistantReply second = await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
        Require(second.ActionProposal is
        {
            IsPlanStep: true,
            PlanStepNumber: 2,
            PlanStepCount: 2
        }, "Skill did not advance through planner.");
        Require(open.Executions == 1 && focus.Preparations == 1 && focus.Executions == 0,
            "Skill did not fresh-route step 2 after step 1.");

        AssistantReply completed = await assistant.ConfirmActionAsync(second.ActionProposal!.Id);
        Require(completed.ActionProposal is null &&
                !assistant.HasPendingPlan &&
                !assistant.HasPendingAction &&
                open.Executions == 1 &&
                focus.Executions == 1,
            "Skill workflow remained pending or executed incorrectly.");
        Require(handler.Calls == 0, "Local skill called Gemini.");
        Require(assistant.Conversation.GetRecentContext().Count == 0,
            "Skill leaked into Gemini context.");

        AssistantReply missing = await assistant.SendAsync(
            new AssistantRequest("jalankan skill tidak-ada"));
        Require(missing.Backend == AssistantBackend.Local &&
                missing.ActionProposal is null &&
                handler.Calls == 0,
            "Unknown skill escaped local handling.");
        Require(assistant.Conversation.GetRecentContext().Count == 0,
            "Unknown skill leaked into Gemini context.");

        assistant.Skills.Register(new RestrictedWorkflowSkill());
        AssistantReply restricted = await assistant.SendAsync(
            new AssistantRequest("jalankan skill restricted-workflow"));
        Require(restricted.Backend == AssistantBackend.Local &&
                restricted.ActionProposal is null &&
                !assistant.HasPendingPlan &&
                !assistant.HasPendingAction &&
                open.Executions == 1 &&
                focus.Executions == 1,
            "Restricted raw skill step bypassed normal routing or execution policy.");
        Require(handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
            "Restricted skill escaped local handling.");
    }

    private static void CheckSkillParserAndRegistry()
    {
        SkillInvocation? invocation = SkillCommandParser.Parse(
            "jalankan skill test-workflow");
        Require(invocation is { Name: "test-workflow", Argument: "", IncludeInContext: false },
            "Skill command was not parsed.");
        SkillInvocation? search = SkillCommandParser.Parse(
            "jalankan skill search-downloads dengan laporan 2026");
        Require(search is { Name: "search-downloads", Argument: "laporan 2026" },
            "Skill argument was not preserved.");
        Require(SkillCommandParser.Parse("run skill test-workflow with report") is
            { Name: "test-workflow", Argument: "report" },
            "English skill syntax was not parsed.");
        foreach (string malformed in new[]
                 {
                     "jalankan skill", "jalankan skill test-workflow laporan",
                     "run skill test-workflow with "
                 })
            Require(SkillCommandParser.Parse(malformed) is null,
                "Malformed skill command was accepted.");

        var router = new AssistantSkillRouter(new IAssistantSkill[]
        {
            new TestWorkflowSkill()
        });
        Require(router.RegisteredSkills.SequenceEqual(new[] { "test-workflow" }),
            "Skill registry did not expose its canonical id.");
        Require(router.Expand(new SkillInvocation("test-flow")).Plan?.Count == 2,
            "Skill alias did not resolve.");
        Require(!router.Expand(new SkillInvocation("missing")).Success,
            "Unknown skill was accepted.");

        var empty = new AssistantSkillRouter(new IAssistantSkill[]
        {
            new InvalidPlanSkill("empty", Array.Empty<AssistantPlanStep>())
        });
        Require(!empty.Expand(new SkillInvocation("empty")).Success,
            "Empty skill plan was accepted.");
        var oversized = new AssistantSkillRouter(new IAssistantSkill[]
        {
            new InvalidPlanSkill("oversized", new[]
            {
                new AssistantPlanStep(0,
                    new string('x', LocalMultiStepPlanParser.MaxStepLength + 1))
            })
        });
        Require(!oversized.Expand(new SkillInvocation("oversized")).Success,
            "Oversized skill step was accepted.");

        AssistantIntent valid = new AssistantIntentRouter().Route(
            "jalankan skill test-workflow");
        Require(valid.Kind == AssistantIntentKind.Skill && valid.Skill?.Name == "test-workflow",
            "Skill intent was not routed before desktop commands.");
        AssistantIntent invalid = new AssistantIntentRouter().Route(
            "jalankan skill test-workflow tanpa-prefix");
        Require(invalid.Kind == AssistantIntentKind.LocalResponse &&
                !invalid.IncludeLocalResponseInContext,
            "Malformed skill command escaped local handling.");

        var builtIn = new SearchDownloadsSkill();
        SkillExpansionResult expanded = builtIn.Expand(
            new SkillInvocation("search-downloads", "invoice 2026"));
        Require(expanded.Plan?.Steps.Select(x => x.Command).SequenceEqual(
                    new[] { "buka downloads", "cari invoice 2026 di downloads" }) == true,
            "Search Downloads emitted the wrong raw commands.");
        Require(!builtIn.Expand(new SkillInvocation("search-downloads")).Success,
            "Search Downloads accepted an empty query.");
    }
}
