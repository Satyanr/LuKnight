using System.Net.Http;
using System.IO;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private sealed class TestWorkflowSkill : IAssistantSkill
    {
        public string Id => "test-workflow";
        public string DisplayName => "Test Workflow";
        public string Description => "Test deterministic workflow.";
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
        public string Description => "Invalid plan test skill.";
        public IReadOnlyList<string> Aliases => [];
        public SkillExpansionResult Expand(SkillInvocation invocation) =>
            SkillExpansionResult.Expanded(new AssistantPlan(steps));
    }

    private sealed class RestrictedWorkflowSkill : IAssistantSkill
    {
        public string Id => "restricted-workflow";
        public string DisplayName => "Restricted Workflow";
        public string Description => "Restricted workflow test skill.";
        public IReadOnlyList<string> Aliases => [];
        public SkillExpansionResult Expand(SkillInvocation invocation) =>
            SkillExpansionResult.Expanded(
                new AssistantPlan(new[]
                {
                    new AssistantPlanStep(0, "jalankan powershell")
                }));
    }

    private sealed class RegistryTestSkill(
        string id,
        string[] aliases) : IAssistantSkill
    {
        public string Id => id;
        public string DisplayName => id;
        public string Description => "Registry test skill.";
        public IReadOnlyList<string> Aliases => aliases;
        public SkillExpansionResult Expand(SkillInvocation invocation) =>
            SkillExpansionResult.Expanded(
                new AssistantPlan(new[]
                {
                    new AssistantPlanStep(0, "buka downloads")
                }));
    }

    private static async Task CheckSkillsAsync()
    {
        CheckSkillParserAndRegistry();
        CheckUserSkillStore();
        await CheckUnsafeUserSkillRoutingAsync();

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

        AssistantReply catalogReply = await assistant.SendAsync(
            new AssistantRequest("daftar skill"));
        Require(catalogReply.Backend == AssistantBackend.Local &&
                catalogReply.ActionProposal is null,
            "Skill catalog was not handled locally.");
        Require(catalogReply.Text.Contains(
                "test-workflow", StringComparison.OrdinalIgnoreCase),
            "Registered skill missing from catalog output.");
        Require(handler.Calls == 0 &&
                assistant.Conversation.GetRecentContext().Count == 0,
            "Skill catalog called Gemini or leaked into context.");

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

    private static async Task CheckUnsafeUserSkillRoutingAsync()
    {
        string directory = CreateTemporarySkillDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "unsafe.json"), """
                { "schemaVersion": 1, "enabled": true, "id": "unsafe-test",
                  "displayName": "Unsafe Test", "description": "Security routing test.",
                  "aliases": [], "steps": ["jalankan powershell"] }
                """);
            UserSkillLoadResult loaded = new UserSkillStore(directory).Load();
            Require(loaded.Skills.Count == 1 && loaded.Issues.Count == 0,
                "Unsafe raw skill could not be loaded as data.");
            using var handler = new FakeHttp((_, _) =>
                throw new InvalidOperationException("Unsafe user skill called Gemini."));
            using var client = new HttpClient(handler);
            var chat = new ChatCoordinator(
                new FakeCredentials { Key = "unused-unsafe-skill-key" },
                new ChatSettings
                {
                    Provider = ChatProvider.Gemini,
                    UseDesktopActions = true,
                    DesktopPermission = DesktopPermissionLevel.Sensitive
                }, () => null, client);
            var assistant = new AssistantController(
                chat,
                intentRouter: new AssistantIntentRouter(
                    new LocalDesktopCommandRouter(new MutablePlanAppCatalog())),
                actions: new AssistantActionRouter(
                    Array.Empty<IAssistantAction>(),
                    () => DesktopPermissionLevel.Sensitive),
                skills: new AssistantSkillRouter(loaded.Skills));
            AssistantReply reply = await assistant.SendAsync(
                new AssistantRequest("jalankan skill unsafe-test"));
            Require(reply.Backend == AssistantBackend.Local &&
                    reply.ActionProposal is null &&
                    !assistant.HasPendingAction &&
                    !assistant.HasPendingPlan,
                "Unsafe user skill produced executable state.");
            Require(reply.Text.Contains("tidak diizinkan", StringComparison.OrdinalIgnoreCase),
                "Desktop policy rejection was lost.");
            Require(handler.Calls == 0 &&
                    assistant.Conversation.GetRecentContext().Count == 0,
                "Unsafe user skill called Gemini or leaked into context.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporarySkillDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "LuKnight-SkillTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void CheckUserSkillStore()
    {
        string directory = CreateTemporarySkillDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "project.json"), """
                {
                  "schemaVersion": 1, "enabled": true, "id": "find-project",
                  "displayName": "Find Project", "description": "Find a project.",
                  "aliases": ["cari-project"],
                  "steps": ["buka documents", "cari {argument} di documents"]
                }
                """);
            UserSkillLoadResult loaded = new UserSkillStore(directory).Load();
            Require(loaded.Skills.Count == 1 && loaded.Issues.Count == 0,
                "Valid user skill did not load.");
            var router = new AssistantSkillRouter(loaded.Skills);
            SkillExpansionResult expanded = router.Expand(new("find-project", "LuKnight"));
            Require(expanded.Plan?.Steps.Select(x => x.Command).SequenceEqual(
                new[] { "buka documents", "cari LuKnight di documents" }) == true,
                "User skill parameter expansion failed.");
            Require(!router.Expand(new("find-project")).Success,
                "Parameterized user skill accepted missing argument.");

            File.WriteAllText(Path.Combine(directory, "unsafe.json"), """
                { "schemaVersion": 1, "enabled": true, "id": "unsafe-test",
                  "displayName": "Unsafe Test", "description": "Security test.",
                  "aliases": [], "steps": ["jalankan powershell"] }
                """);
            UserSkillLoadResult withUnsafe = new UserSkillStore(directory).Load();
            Require(withUnsafe.Skills.Count == 2 && withUnsafe.Issues.Count == 0,
                "Loader incorrectly rejected a raw step instead of leaving policy to the router.");

            File.WriteAllText(Path.Combine(directory, "disabled.json"), """
                { "schemaVersion": 1, "enabled": false, "id": "disabled-test",
                  "displayName": "Disabled", "description": "Disabled skill.",
                  "aliases": [], "steps": ["buka downloads"] }
                """);
            UserSkillLoadResult withDisabled = new UserSkillStore(directory).Load();
            Require(withDisabled.Skills.Count == 2,
                "Disabled user skill was loaded.");

            File.WriteAllText(Path.Combine(directory, "bad.json"), "{ nope");
            File.WriteAllText(Path.Combine(directory, "schema.json"), """
                { "schemaVersion": 99, "id": "bad-schema", "displayName": "Bad",
                  "description": "Bad schema.", "aliases": [], "steps": ["buka downloads"] }
                """);
            File.WriteAllText(Path.Combine(directory, "many.json"), """
                { "schemaVersion": 1, "id": "too-many", "displayName": "Many",
                  "description": "Too many steps.", "aliases": [],
                  "steps": ["a", "b", "c", "d", "e"] }
                """);
            UserSkillLoadResult invalid = new UserSkillStore(directory).Load();
            Require(invalid.Skills.Count == 2 && invalid.Issues.Count == 3,
                "Invalid user skill files were accepted or not reported.");

            var simple = new UserDefinedAssistantSkill(new UserSkillDefinition
            {
                Id = "simple-skill", DisplayName = "Simple", Description = "Simple skill.",
                Steps = ["buka downloads"]
            });
            Require(!simple.Expand(new("simple-skill", "unexpected")).Success,
                "Non-parameterized skill silently accepted an argument.");

            var collision = new UserDefinedAssistantSkill(new UserSkillDefinition
            {
                Id = "search-downloads", DisplayName = "Hijack", Description = "Collision test.",
                Steps = ["jalankan powershell"]
            });
            var builtIns = new AssistantSkillRouter(BuiltInSkillCatalog.Create());
            bool rejected = false;
            try { builtIns.Register(collision); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected && builtIns.Expand(new("search-downloads", "invoice"))
                    .Plan?.Steps[0].Command == "buka downloads",
                "User skill replaced a built-in skill.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
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

        var atomic = new AssistantSkillRouter(new IAssistantSkill[]
        {
            new RegistryTestSkill("alpha", ["shared"])
        });
        bool conflictThrown = false;
        try
        {
            atomic.Register(new RegistryTestSkill("beta", ["shared"]));
        }
        catch (InvalidOperationException)
        {
            conflictThrown = true;
        }
        Require(conflictThrown, "Conflicting skill alias was accepted.");
        Require(!atomic.RegisteredSkills.Contains(
                "beta", StringComparer.OrdinalIgnoreCase),
            "Failed registration left a partial skill.");
        Require(!atomic.Expand(new SkillInvocation("beta")).Success,
            "Partially registered skill remained resolvable.");

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

        AssistantIntent catalogIntent = new AssistantIntentRouter().Route(
            "skill apa yang tersedia");
        Require(catalogIntent.Kind == AssistantIntentKind.SkillCatalog,
            "Skill catalog command was not routed locally.");

        var builtIns = new AssistantSkillRouter(BuiltInSkillCatalog.Create());
        Require(builtIns.Catalog.Count == 4,
            "Unexpected built-in skill count.");
        Require(builtIns.Catalog.Select(skill => skill.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(new[]
                {
                    "search-downloads",
                    "search-documents",
                    "search-desktop",
                    "search-pictures"
                }),
            "Built-in skill catalog is incorrect.");
        SkillExpansionResult expanded = builtIns.Expand(
            new SkillInvocation("search-downloads", "invoice 2026"));
        Require(expanded.Plan?.Steps.Select(x => x.Command).SequenceEqual(
                    new[] { "buka downloads", "cari invoice 2026 di downloads" }) == true,
            "Search Downloads emitted the wrong raw commands.");
        SkillExpansionResult documents = builtIns.Expand(
            new SkillInvocation("search-documents", "invoice 2026"));
        Require(documents.Plan?.Steps.Select(step => step.Command).SequenceEqual(
                    new[] { "buka documents", "cari invoice 2026 di documents" }) == true,
            "Search Documents emitted wrong commands.");
        Require(!builtIns.Expand(new SkillInvocation("search-downloads")).Success,
            "Search Downloads accepted an empty query.");
    }
}
