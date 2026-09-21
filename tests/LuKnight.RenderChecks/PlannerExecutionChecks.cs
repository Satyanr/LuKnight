using System.Net.Http;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private sealed class MutablePlanAppCatalog
        : IDesktopAppCatalog
    {
        public int ResolveCalls;
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
            ResolveCalls++;
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
        public bool IncludeInContext = true;
        public Action? OnExecute;
        public Action? OnPrepare;
        public ActionPreparationResult Prepare(ActionInvocation invocation)
        {
            Preparations++;
            OnPrepare?.Invoke();
            if (ThrowPrepare) throw new OperationCanceledException("Prepare cancelled");
            return new(!FailPrepare, "Preparation result", new(Name, invocation.Arguments, "Test", "Confirm?", IncludeInContext: IncludeInContext, Risk: Risk));
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
        await CheckPlanAdmissionAndProgressAsync();
        await CheckPlanLifetimeAsync();
        await CheckPlannerDoesNotRetryPreparationAsync();
        await CheckRuntimeWindowVariablesAsync();
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

    private sealed class MutablePlanWindowCatalog(DesktopWindowTarget target)
        : IDesktopWindowTargetCatalog
    {
        public DesktopWindowTarget? Target = target;
        public IReadOnlyList<DesktopWindowTarget> Capture() => Target is null ? [] : [Target];
        public DesktopWindowResolution Resolve(string query) =>
            Target is not null && string.Equals(Target.Title, query, StringComparison.OrdinalIgnoreCase)
                ? new(Target, [Target]) : new(null, []);
        public bool TryResolveById(string id, out DesktopWindowTarget target)
        {
            if (Target is not null && string.Equals(Target.Id, id, StringComparison.Ordinal))
            { target = Target; return true; }
            target = default!; return false;
        }
    }

    private sealed class RuntimeWindowTestAction(
        MutablePlanWindowCatalog windows, bool removeAfterFirst = false) : IAssistantAction
    {
        public string Name => BuiltInActionNames.DesktopInvokeUiControl;
        public int Preparations, Executions;
        public string? LastWindowQuery;
        public ActionPreparationResult Prepare(ActionInvocation invocation)
        {
            Preparations++;
            invocation.Arguments.TryGetValue("window", out string? query);
            LastWindowQuery = query;
            DesktopWindowResolution resolved = windows.Resolve(query ?? "");
            if (!resolved.Found || resolved.Match is null)
                return new(false, "Window tidak ditemukan.");
            AssistantActionRisk risk = invocation.Arguments.TryGetValue("query", out string? control) &&
                string.Equals(control, "Save", StringComparison.OrdinalIgnoreCase)
                    ? AssistantActionRisk.Sensitive : AssistantActionRisk.Interaction;
            return new(true, "Prepared.", new PreparedAssistantAction(
                Name, new Dictionary<string, string> { ["windowId"] = resolved.Match.Id },
                "Runtime test", "Confirm?", IncludeInContext: false, Risk: risk));
        }
        public Task<ActionExecutionResult> ExecuteAsync(
            PreparedAssistantAction action, CancellationToken cancellationToken = default)
        {
            Executions++;
            if (Executions == 1)
                windows.Target = removeAfterFirst ? null : windows.Target! with { Title = "After Refresh" };
            return Task.FromResult(new ActionExecutionResult(true, "Executed."));
        }
    }

    private static async Task CheckRuntimeWindowVariablesAsync()
    {
        foreach (bool removeWindow in new[] { false, true })
        {
            var windows = new MutablePlanWindowCatalog(new DesktopWindowTarget(
                (nint)123, 456, "fixture", "Before Refresh", 0, false, true));
            var action = new RuntimeWindowTestAction(windows, removeWindow);
            var skill = new UserDefinedAssistantSkill(new UserSkillDefinition
            {
                SchemaVersion = 3, Id = "runtime-test", DisplayName = "Runtime Test",
                Description = "Runtime test.", Parameters = [],
                Steps =
                [
                    "klik tombol Refresh di window Before Refresh",
                    "klik tombol Save di window {last.window}"
                ]
            });
            using var handler = new FakeHttp((_, _) =>
                throw new InvalidOperationException("Runtime workflow called Gemini."));
            using var client = new HttpClient(handler);
            var chat = new ChatCoordinator(
                new FakeCredentials { Key = "unused-runtime-key" },
                new ChatSettings
                {
                    Provider = ChatProvider.Gemini, UseDesktopActions = true,
                    DesktopPermission = DesktopPermissionLevel.Sensitive
                }, () => null, client);
            var assistant = new AssistantController(
                chat,
                intentRouter: new AssistantIntentRouter(
                    new LocalDesktopCommandRouter(new MutablePlanAppCatalog(), windows)),
                actions: new AssistantActionRouter(
                    new IAssistantAction[] { action },
                    () => DesktopPermissionLevel.Sensitive),
                skills: new AssistantSkillRouter(new[] { skill }));
            AssistantReply first = await assistant.SendAsync(
                new AssistantRequest("jalankan skill runtime-test"));
            Require(first.ActionProposal is { PlanStepNumber: 1, PlanStepCount: 2 },
                "Runtime workflow did not start.");
            AssistantReply next = await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
            Require(action.Executions == 1, "Runtime step 1 did not execute.");
            if (removeWindow)
                Require(action.Preparations == 1 && next.ActionProposal is null &&
                        !assistant.HasPendingPlan && !assistant.HasPendingAction,
                    "Missing runtime variable reached preparation or remained pending.");
            else
            {
                Require(action.Preparations == 2 && string.Equals(
                        action.LastWindowQuery, "After Refresh", StringComparison.OrdinalIgnoreCase),
                    $"Runtime {{last.window}} did not use fresh window state: preparations={action.Preparations}, query='{action.LastWindowQuery}', reply='{next.Text}'.");
                Require(next.ActionProposal is
                {
                    PlanStepNumber: 2,
                    Risk: AssistantActionRisk.Sensitive,
                    ConfirmationStage: AssistantConfirmationStage.SensitiveReview
                }, "Runtime step 2 lost Sensitive classification.");
            }
            Require(handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
                "Runtime workflow leaked Gemini/context.");
        }
    }

    private static async Task CheckPlannerDoesNotRetryPreparationAsync()
    {
        var catalog = new MutablePlanAppCatalog();
        catalog.Add(new DesktopAppTarget(
            "launcher", "Launcher", "launcher", ["launcher"], ["launcher"],
            DesktopAppSource.BuiltIn));
        var open = new PlanTestAction(BuiltInActionNames.DesktopOpenApplication);
        var focus = new PlanTestAction(BuiltInActionNames.DesktopFocusApplication)
        {
            FailPrepare = true
        };
        using var handler = new FakeHttp((_, _) =>
            throw new InvalidOperationException("No-retry planner test called Gemini."));
        using var client = new HttpClient(handler);
        var chat = new ChatCoordinator(
            new FakeCredentials { Key = "unused-no-retry-key" },
            new ChatSettings
            {
                Provider = ChatProvider.Gemini,
                UseDesktopActions = true,
                DesktopPermission = DesktopPermissionLevel.Sensitive
            }, () => null, client);
        var assistant = new AssistantController(
            chat,
            intentRouter: new AssistantIntentRouter(new LocalDesktopCommandRouter(catalog)),
            actions: new AssistantActionRouter(
                new IAssistantAction[] { open, focus },
                () => DesktopPermissionLevel.Sensitive));
        AssistantReply first = await assistant.SendAsync(
            new AssistantRequest("buka launcher lalu fokus launcher"));
        Require(first.ActionProposal is not null,
            "No-retry test did not prepare first step.");
        AssistantReply failed = await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
        Require(open.Executions == 1, "First step did not execute.");
        Require(focus.Preparations == 1,
            "Failed preparation was automatically retried.");
        Require(focus.Executions == 0, "Failed preparation reached execution.");
        Require(failed.ActionProposal is null &&
                !assistant.HasPendingPlan && !assistant.HasPendingAction,
            "Failed preparation did not stop the plan.");
        Require(handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
            "No-retry handling leaked Gemini/context.");
    }

    private static async Task CheckPlanAdmissionAndProgressAsync()
    {
        foreach (string input in new[] { "aku suka kopi lalu teh", "ceritakan lelucon lalu cerita lain", "jalankan powershell lalu buka chrome" })
        {
            string? body = null;
            using var handler = new FakeHttp(async (request, token) =>
            {
                body = await request.Content!.ReadAsStringAsync(token);
                return JsonResponse(new { candidates = new[] { new { content = new { parts = new[] { new { text = "Conversation reply" } } } } } });
            });
            using var client = new HttpClient(handler);
            var chat = new ChatCoordinator(new FakeCredentials { Key = "unused-plan-key" },
                new ChatSettings { Provider = ChatProvider.Gemini, UseDesktopActions = true }, () => null, client);
            var assistant = new AssistantController(chat);
            var reply = await assistant.SendAsync(new AssistantRequest(input));
            Require(!assistant.HasPendingPlan && reply.ActionProposal is null, "Admission created unexpected plan.");
            if (input.StartsWith("jalankan", StringComparison.Ordinal))
                Require(reply.Backend == AssistantBackend.Local && handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0, "Restricted first step escaped locally.");
            else
                Require(reply.Backend == AssistantBackend.Gemini && handler.Calls == 1 && body!.Contains(input, StringComparison.Ordinal), "Ordinary conversation was not sent whole.");
        }

        foreach (bool sensitive in new[] { false, true })
        {
            var catalog = new MutablePlanAppCatalog();
            catalog.Add(new("launcher", "Launcher", "launcher", new[] { "launcher" }, new[] { "launcher" }, DesktopAppSource.BuiltIn));
            var open = new PlanTestAction(BuiltInActionNames.DesktopOpenApplication);
            var focus = new PlanTestAction(BuiltInActionNames.DesktopFocusApplication) { Risk = sensitive ? AssistantActionRisk.Sensitive : AssistantActionRisk.Navigation };
            using var handler = new FakeHttp((_, _) => throw new InvalidOperationException("Plan UX called Gemini."));
            using var client = new HttpClient(handler);
            var chat = new ChatCoordinator(new FakeCredentials { Key = "unused-plan-key" },
                new ChatSettings { Provider = ChatProvider.Gemini, UseDesktopActions = true }, () => null, client);
            var assistant = new AssistantController(chat,
                intentRouter: new AssistantIntentRouter(new LocalDesktopCommandRouter(catalog)),
                actions: new AssistantActionRouter(new IAssistantAction[] { open, focus }, () => DesktopPermissionLevel.Sensitive));
            void CheckProposal(AssistantActionProposal? proposal, int step)
            {
                Require(proposal is { IsPlanStep: true, PlanStepCount: 3 } && proposal.PlanStepNumber == step, "Plan metadata incorrect.");
                Require(proposal!.Title.StartsWith($"Rencana {step}/3", StringComparison.Ordinal) &&
                    proposal.ConfirmationText.Contains($"Ini hanya mengizinkan langkah {step} dari 3", StringComparison.Ordinal), "Per-step authorization notice missing.");
            }
            var first = await assistant.SendAsync(new AssistantRequest("buka launcher lalu fokus launcher lalu buka launcher"));
            CheckProposal(first.ActionProposal, 1);
            Require(catalog.ResolveCalls == 1, "First step routed more than once during admission.");
            var second = await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
            CheckProposal(second.ActionProposal, 2);
            Require(open.Executions == 1 && focus.Executions == 0 && first.ActionProposal.Id != second.ActionProposal!.Id, "First Yes advanced too far.");
            if (!sensitive)
            {
                assistant.CancelAction(second.ActionProposal!.Id);
                Require(open.Executions == 1 && focus.Executions == 0, "Cancel executed remaining actions.");
            }
            else
            {
                Require(second.ActionProposal!.ConfirmationStage == AssistantConfirmationStage.SensitiveReview, "Sensitive review missing.");
                var final = await assistant.ConfirmActionAsync(second.ActionProposal.Id);
                CheckProposal(final.ActionProposal, 2);
                Require(final.ActionProposal!.ConfirmationStage == AssistantConfirmationStage.SensitiveFinal && final.ActionProposal.Id != second.ActionProposal.Id && focus.Executions == 0 && open.Executions == 1, "Sensitive review advanced workflow.");
                var third = await assistant.ConfirmActionAsync(final.ActionProposal.Id);
                CheckProposal(third.ActionProposal, 3);
                Require(focus.Executions == 1 && open.Executions == 1, "Sensitive final executed wrong count.");
                await assistant.ConfirmActionAsync(third.ActionProposal!.Id);
                Require(open.Executions == 2, "Third action did not execute.");
            }
            Require(!assistant.HasPendingPlan && !assistant.HasPendingAction && handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0, "Plan UX state/privacy failure.");
            var standalone = await assistant.SendAsync(new AssistantRequest("buka launcher"));
            Require(standalone.ActionProposal is { IsPlanStep: false, PlanStepNumber: null, PlanStepCount: null }, "Standalone action received plan metadata.");
            assistant.CancelAction(standalone.ActionProposal!.Id);
        }
    }

    private static async Task CheckPlanLifetimeAsync()
    {
        foreach (string scenario in new[] { "expired", "recover", "recover-proposal", "proposal-boundary", "during-preparation", "during-execution", "sensitive-expired", "clamp", "final-boundary" })
        {
            DateTimeOffset now = new(2026, 9, 14, 12, 0, 0, TimeSpan.Zero);
            DateTimeOffset start = now;
            var catalog = new MutablePlanAppCatalog();
            catalog.Add(new("launcher", "Launcher", "launcher", new[] { "launcher" }, new[] { "launcher" }, DesktopAppSource.BuiltIn));
            var open = new PlanTestAction(BuiltInActionNames.DesktopOpenApplication);
            var focus = new PlanTestAction(BuiltInActionNames.DesktopFocusApplication);
            if (scenario is "recover" or "recover-proposal") open.IncludeInContext = false;
            if (scenario == "during-preparation") open.OnPrepare = () => now = start.AddMinutes(5);
            if (scenario == "sensitive-expired") open.Risk = AssistantActionRisk.Sensitive;
            if (scenario is "clamp" or "final-boundary")
            {
                focus.Risk = AssistantActionRisk.Sensitive;
                open.OnExecute = () => now = start.AddMinutes(5).AddSeconds(-10);
            }
            if (scenario == "during-execution") open.OnExecute = () => now = start.AddMinutes(6);
            using var handler = new FakeHttp((_, _) => throw new InvalidOperationException("Recovery called Gemini."));
            using var client = new HttpClient(handler);
            var chat = new ChatCoordinator(new FakeCredentials { Key = "unused-plan-key" },
                new ChatSettings { Provider = ChatProvider.Gemini, UseDesktopActions = true }, () => null, client);
            var assistant = new AssistantController(chat,
                intentRouter: new AssistantIntentRouter(new LocalDesktopCommandRouter(catalog)),
                actions: new AssistantActionRouter(new IAssistantAction[] { open, focus }, () => DesktopPermissionLevel.Sensitive), clock: () => now);
            var first = await assistant.SendAsync(new AssistantRequest("buka launcher lalu fokus launcher"));
            if (scenario == "during-preparation")
            {
                Require(first.ActionProposal is null && open.Executions == 0 && focus.Preparations == 0,
                    "Preparation crossing the deadline created a proposal or advanced the plan.");
                Require(!assistant.HasPendingPlan && !assistant.HasPendingAction && handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
                    "Expired preparation retained state or leaked context.");
                continue;
            }
            Require(first.ActionProposal?.ExpiresAt == start.AddMinutes(1), "Standard expiry ignored injected clock.");
            if (scenario is "recover" or "recover-proposal")
            {
                now = start.AddMinutes(scenario == "recover" ? 6 : 1);
                var replacement = await assistant.SendAsync(new AssistantRequest("buka launcher"));
                Require(replacement.ActionProposal is { IsPlanStep: false } && !assistant.HasPendingPlan, "Expired plan blocked replacement.");
                try { await assistant.ConfirmActionAsync(first.ActionProposal!.Id); Require(false, "Stale proposal accepted."); } catch (InvalidOperationException) { }
                assistant.CancelAction(replacement.ActionProposal!.Id);
            }
            else if (scenario is "clamp" or "final-boundary")
            {
                var review = await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
                Require(review.ActionProposal?.ExpiresAt == start.AddMinutes(5), "Standard proposal exceeded absolute deadline.");
                var final = await assistant.ConfirmActionAsync(review.ActionProposal!.Id);
                Require(final.ActionProposal?.ExpiresAt == start.AddMinutes(5) && focus.Executions == 0, "SensitiveFinal extended deadline.");
                now = scenario == "clamp" ? start.AddMinutes(5).AddTicks(-1) : start.AddMinutes(5);
                await assistant.ConfirmActionAsync(final.ActionProposal!.Id);
                Require(focus.Executions == (scenario == "clamp" ? 1 : 0), "Absolute deadline boundary incorrect.");
            }
            else
            {
                if (scenario != "during-execution") now = scenario == "proposal-boundary" ? start.AddMinutes(1) : start.AddMinutes(6);
                var expired = await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
                Require(expired.ActionProposal is null && focus.Preparations == 0 && focus.Executions == 0, "Expired plan advanced.");
                Require(open.Executions == (scenario == "during-execution" ? 1 : 0), "Expiry changed execution accounting.");
                if (scenario == "during-execution") Require(expired.Text.Contains("selesai", StringComparison.Ordinal), "Completed action reported as cancelled.");
            }
            Require(!assistant.HasPendingPlan && !assistant.HasPendingAction && handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0, "Recovery state/privacy failure: " + scenario);
        }
    }
}
