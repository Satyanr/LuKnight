using System.Text;
using System.Text.Json;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LuKnight.Assistant;
using LuKnight.Models;
using LuKnight.Services;

internal static partial class Program
{
    private static async Task CheckUserSkillLiveAsync()
    {
        string token = Guid.NewGuid().ToString("N")[..8];
        string initialTitle = $"{UiFixturePrefix} {token}";
        string refreshTitle = $"{initialTitle} — REFRESH INVOKED";
        string saveTitle = $"{initialTitle} — SAVE INVOKED";
        string skillDirectory = Path.Combine(Path.GetTempPath(),
            "LuKnight-UserSkillLive", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(skillDirectory);
        var definition = new UserSkillDefinition
        {
            Id = "fixture-workflow",
            DisplayName = "Fixture Workflow",
            Description = "Native user-defined skill acceptance workflow.",
            Aliases = ["fixture-flow"],
            Steps =
            [
                $"klik tombol Refresh di window {initialTitle}",
                $"klik tombol Save di window {refreshTitle}"
            ]
        };
        File.WriteAllText(
            Path.Combine(skillDirectory, "fixture-workflow.json"),
            JsonSerializer.Serialize(definition, SettingsService.JsonOptions));
        using Process fixture = StartUiFixtureProcess(token);
        try
        {
            UserSkillLoadResult loaded = new UserSkillStore(skillDirectory).Load();
            Require(loaded.Skills.Count == 1 && loaded.Issues.Count == 0,
                "Native user skill JSON was not loaded.");
            var skills = new AssistantSkillRouter(BuiltInSkillCatalog.Create());
            foreach (IAssistantSkill skill in loaded.Skills) skills.Register(skill);
            Require(skills.Catalog.Any(item => item.Id == "fixture-workflow"),
                "User-defined skill missing from runtime catalog.");

            var windows = new DesktopWindowTargetService();
            await WaitForFixtureWindowAsync(windows, fixture, initialTitle);
            var ui = new WindowsDesktopUiAutomationReader();
            var sequence = new List<string>();
            var invoke = new RecordingUiActionExecutor(
                new WindowsDesktopUiActionExecutor(), sequence);
            var mouse = new RecordingMouseActionExecutor(
                new WindowsDesktopMouseActionExecutor(), sequence);
            using var handler = new FakeHttp((_, _) =>
                throw new InvalidOperationException("Native user skill attempted Gemini."));
            using var client = new HttpClient(handler);
            var chat = new ChatCoordinator(
                new FakeCredentials { Key = "unused-user-skill-live-key" },
                new ChatSettings
                {
                    Provider = ChatProvider.Gemini,
                    UseDesktopActions = true,
                    DesktopPermission = DesktopPermissionLevel.Sensitive
                }, () => null, client);
            var desktopRouter = new LocalDesktopCommandRouter(
                new DesktopAppCatalogService(() => Array.Empty<DesktopAppTarget>()), windows);
            var intentRouter = new AssistantIntentRouter(desktopRouter);
            var uiAction = new InvokeDesktopUiControlAction(
                () => chat.Options.UseDesktopActions, windows, ui, invoke, mouse);
            var actions = new AssistantActionRouter(
                new IAssistantAction[] { uiAction },
                () => chat.Options.DesktopPermission);
            var assistant = new AssistantController(
                chat, intentRouter: intentRouter, actions: actions, skills: skills);

            AssistantReply first = await assistant.SendAsync(
                new AssistantRequest("jalankan skill fixture-flow"));
            Require(first.ActionProposal is
            {
                IsPlanStep: true, PlanStepNumber: 1, PlanStepCount: 2,
                Risk: AssistantActionRisk.Interaction,
                ConfirmationStage: AssistantConfirmationStage.Standard
            }, "User skill did not enter native planner step 1/2.");
            Require(invoke.Calls == 0 && mouse.Calls == 0,
                "User skill executed before confirmation.");
            Require(handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
                "User skill preparation leaked Gemini/context.");
            Require(!windows.Capture().Any(window =>
                    window.ProcessId == fixture.Id &&
                    string.Equals(window.Title, refreshTitle, StringComparison.Ordinal)),
                "Future user-skill target existed before step 1.");

            AssistantReply review = await assistant.ConfirmActionAsync(first.ActionProposal!.Id);
            Require(invoke.Calls == 1 && mouse.Calls == 0 &&
                    sequence.SequenceEqual(new[] { "uia" }),
                "Refresh was not native UIA-only.");
            await WaitForFixtureWindowAsync(windows, fixture, refreshTitle);
            Require(review.ActionProposal is
            {
                IsPlanStep: true, PlanStepNumber: 2, PlanStepCount: 2,
                Risk: AssistantActionRisk.Sensitive,
                ConfirmationStage: AssistantConfirmationStage.SensitiveReview
            }, "User skill step 2 was not freshly routed as Sensitive.");

            Guid reviewId = review.ActionProposal!.Id;
            AssistantReply final = await assistant.ConfirmActionAsync(reviewId);
            Require(final.ActionProposal is
            {
                IsPlanStep: true, PlanStepNumber: 2, PlanStepCount: 2,
                Risk: AssistantActionRisk.Sensitive,
                ConfirmationStage: AssistantConfirmationStage.SensitiveFinal
            }, "User skill SensitiveReview did not produce final confirmation.");
            Require(final.ActionProposal!.Id != reviewId &&
                    invoke.Calls == 1 && mouse.Calls == 0 && sequence.Count == 1,
                "First Sensitive confirmation executed or reused its ID.");

            AssistantReply completed = await assistant.ConfirmActionAsync(final.ActionProposal.Id);
            Require(completed.ActionProposal is null && invoke.Calls == 2 &&
                    mouse.Calls == 0 && sequence.SequenceEqual(new[] { "uia", "uia" }),
                "User skill native execution did not complete in UIA order.");
            await WaitForFixtureWindowAsync(windows, fixture, saveTitle);
            Require(!assistant.HasPendingAction && !assistant.HasPendingPlan,
                "User skill remained pending after completion.");
            Require(handler.Calls == 0 && assistant.Conversation.GetRecentContext().Count == 0,
                "Native user skill called Gemini or leaked into context.");

            Console.WriteLine();
            Console.WriteLine("User JSON skill loaded.");
            Console.WriteLine("Step 1/2: native Refresh UIA Invoke.");
            Console.WriteLine("Step 2 freshly routed after native state change.");
            Console.WriteLine("Step 2/2: Sensitive Save.");
            Console.WriteLine("Sensitive review: no execution.");
            Console.WriteLine("Sensitive final: native Save UIA Invoke.");
            Console.WriteLine("Gemini calls: 0.");
            Console.WriteLine();
            Console.WriteLine("PASS: native user-defined skill acceptance.");
        }
        finally
        {
            await StopUiFixtureAsync(fixture);
            try { Directory.Delete(skillDirectory, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Console.WriteLine($"Warning: temporary skill folder cleanup failed: {ex.Message}"); }
        }
    }

    private static async Task
        CheckPlannerLiveAsync()
    {
        string token =
            Guid.NewGuid()
                .ToString("N")[..8];

        string initialTitle =
            $"{UiFixturePrefix} {token}";

        string refreshTitle =
            $"{initialTitle} — REFRESH INVOKED";

        string saveTitle =
            $"{initialTitle} — SAVE INVOKED";

        using Process fixture =
            StartUiFixtureProcess(
                token);

        try
        {
            var windows =
                new DesktopWindowTargetService();

            DesktopWindowTarget target =
                await WaitForFixtureWindowAsync(
                    windows,
                    fixture,
                    initialTitle);

            var ui =
                new WindowsDesktopUiAutomationReader();

            DesktopUiSnapshot snapshot =
                await ui.CaptureAsync(
                    target);

            Require(
                snapshot.Success,
                snapshot.Error ??
                    "Planner native fixture capture failed.");

            DesktopUiControlResolution refresh =
                DesktopUiControlResolver.Resolve(
                    snapshot,
                    "Refresh",
                    "Button");

            DesktopUiControlResolution save =
                DesktopUiControlResolver.Resolve(
                    snapshot,
                    "Save",
                    "Button");

            Require(
                refresh.Match is not null,
                "Refresh button was not visible through native UIA.");

            Require(
                save.Match is not null,
                "Save button was not visible through native UIA.");

            Require(
                DesktopUiActionRiskClassifier
                    .ClassifyButton(
                        refresh.Match!,
                        target)
                    .Risk ==
                AssistantActionRisk.Interaction,
                "Refresh did not classify as Interaction.");

            Require(
                DesktopUiActionRiskClassifier
                    .ClassifyButton(
                        save.Match!,
                        target)
                    .Risk ==
                AssistantActionRisk.Sensitive,
                "Save did not classify as Sensitive.");


            var sequence =
                new List<string>();

            var invoke =
                new RecordingUiActionExecutor(
                    new WindowsDesktopUiActionExecutor(),
                    sequence);

            var mouse =
                new RecordingMouseActionExecutor(
                    new WindowsDesktopMouseActionExecutor(),
                    sequence);


            using var handler =
                new FakeHttp(
                    (_, _) =>
                        throw new InvalidOperationException(
                            "Native planner attempted Gemini."));

            using var client =
                new HttpClient(
                    handler);

            var chat =
                new ChatCoordinator(
                    new FakeCredentials
                    {
                        Key =
                            "unused-planner-live-key"
                    },
                    new ChatSettings
                    {
                        Provider =
                            ChatProvider.Gemini,

                        UseDesktopActions =
                            true,

                        DesktopPermission =
                            DesktopPermissionLevel
                                .Sensitive
                    },
                    () => null,
                    client);


            var desktopRouter =
                new LocalDesktopCommandRouter(
                    new DesktopAppCatalogService(
                        () =>
                            Array.Empty<
                                DesktopAppTarget>()),
                    windows);

            var intentRouter =
                new AssistantIntentRouter(
                    desktopRouter);

            var action =
                new InvokeDesktopUiControlAction(
                    () =>
                        chat.Options
                            .UseDesktopActions,
                    windows,
                    ui,
                    invoke,
                    mouse);

            var actions =
                new AssistantActionRouter(
                    new IAssistantAction[]
                    {
                        action
                    },
                    () =>
                        chat.Options
                            .DesktopPermission);

            var assistant =
                new AssistantController(
                    chat,
                    intentRouter:
                        intentRouter,
                    actions:
                        actions);


            string command =
                $"klik tombol Refresh di window {initialTitle} " +
                $"lalu klik tombol Save di window {refreshTitle}";


            AssistantReply first =
                await assistant.SendAsync(
                    new AssistantRequest(
                        command));

            Require(
                first.Backend ==
                    AssistantBackend.Local,
                "Native planner was not local.");

            Require(
                first.ActionProposal is
                {
                    IsPlanStep:
                        true,

                    PlanStepNumber:
                        1,

                    PlanStepCount:
                        2,

                    Risk:
                        AssistantActionRisk.Interaction,

                    ConfirmationStage:
                        AssistantConfirmationStage.Standard
                },
                "Planner did not produce step 1/2 confirmation.");

            Require(
                invoke.Calls == 0 &&
                mouse.Calls == 0 &&
                sequence.Count == 0,
                "Planner executed before first confirmation.");

            Require(
                assistant.HasPendingPlan &&
                assistant.HasPendingAction,
                "Native plan state was not retained.");

            Require(
                handler.Calls == 0,
                "Plan preparation called Gemini.");

            Require(
                assistant.Conversation
                    .GetRecentContext()
                    .Count == 0,
                "Plan preparation leaked into Gemini context.");


            // Step 2 deliberately targets refreshTitle.
            // That window title does not exist yet.
            Require(
                !windows.Capture()
                    .Any(
                        item =>
                            item.ProcessId ==
                                fixture.Id &&
                            string.Equals(
                                item.Title,
                                refreshTitle,
                                StringComparison.Ordinal)),
                "Future-step target already existed before step 1.");


            Guid firstId =
                first.ActionProposal!.Id;

            AssistantReply second =
                await assistant
                    .ConfirmActionAsync(
                        firstId);

            Require(
                invoke.Calls == 1,
                "Refresh was not invoked exactly once.");

            Require(
                mouse.Calls == 0,
                "Refresh unexpectedly used mouse fallback.");

            Require(
                sequence.Count == 1 &&
                sequence[0] ==
                    "uia",
                "Unexpected execution sequence after step 1.");

            await WaitForFixtureWindowAsync(
                windows,
                fixture,
                refreshTitle);


            // If step 2 had been resolved before Refresh,
            // this proposal could not exist because refreshTitle
            // did not exist at plan creation time.
            Require(
                second.ActionProposal is
                {
                    IsPlanStep:
                        true,

                    PlanStepNumber:
                        2,

                    PlanStepCount:
                        2,

                    Risk:
                        AssistantActionRisk.Sensitive,

                    ConfirmationStage:
                        AssistantConfirmationStage.SensitiveReview
                },
                $"Step 2 was not freshly routed after Refresh. Reply: {second.Text}; proposal: {second.ActionProposal}");

            Require(
                second.ActionProposal!.Id !=
                    firstId,
                "Step 2 reused step 1 proposal id.");

            Require(
                invoke.Calls == 1 &&
                mouse.Calls == 0,
                "Save executed before Sensitive review.");


            Guid reviewId =
                second.ActionProposal.Id;

            AssistantReply final =
                await assistant
                    .ConfirmActionAsync(
                        reviewId);

            Require(
                final.ActionProposal is
                {
                    IsPlanStep:
                        true,

                    PlanStepNumber:
                        2,

                    PlanStepCount:
                        2,

                    Risk:
                        AssistantActionRisk.Sensitive,

                    ConfirmationStage:
                        AssistantConfirmationStage.SensitiveFinal
                },
                "Sensitive review did not produce final confirmation.");

            Require(
                final.ActionProposal!.Id !=
                    reviewId,
                "SensitiveFinal reused review id.");

            Require(
                invoke.Calls == 1 &&
                mouse.Calls == 0 &&
                sequence.Count == 1,
                "First Sensitive Yes reached native executor.");

            Require(
                windows.Capture()
                    .Any(
                        item =>
                            item.ProcessId ==
                                fixture.Id &&
                            string.Equals(
                                item.Title,
                                refreshTitle,
                                StringComparison.Ordinal)),
                "Sensitive review changed fixture state.");


            AssistantReply finished =
                await assistant
                    .ConfirmActionAsync(
                        final.ActionProposal.Id);

            Require(
                finished.ActionProposal is null,
                "Completed native plan returned another proposal.");

            Require(
                invoke.Calls == 2,
                "Native planner did not invoke exactly two UIA actions.");

            Require(
                mouse.Calls == 0,
                "Native plan unexpectedly used mouse fallback.");

            Require(
                sequence.Count == 2 &&
                sequence[0] == "uia" &&
                sequence[1] == "uia",
                "Native planner execution order was incorrect.");

            await WaitForFixtureWindowAsync(
                windows,
                fixture,
                saveTitle);


            Require(
                !assistant.HasPendingPlan &&
                !assistant.HasPendingAction,
                "Native plan remained pending after completion.");

            Require(
                handler.Calls == 0,
                "Native plan called Gemini.");

            Require(
                assistant.Conversation
                    .GetRecentContext()
                    .Count == 0,
                "Native plan leaked into Gemini context.");


            Console.WriteLine();
            Console.WriteLine(
                "Plan step 1/2: Refresh prepared.");

            Console.WriteLine(
                "First Yes: native Refresh UIA Invoke.");

            Console.WriteLine(
                "Step 2 freshly resolved after title changed.");

            Console.WriteLine(
                "Plan step 2/2: Save classified Sensitive.");

            Console.WriteLine(
                "Sensitive review: no execution.");

            Console.WriteLine(
                "Sensitive final: native Save UIA Invoke.");

            Console.WriteLine(
                "Execution sequence: UIA -> UIA.");

            Console.WriteLine(
                "Gemini calls: 0.");

            Console.WriteLine();
            Console.WriteLine(
                "PASS: native multi-step planner acceptance.");
        }
        finally
        {
            await StopUiFixtureAsync(
                fixture);
        }
    }

    private static async Task
        CheckSensitivePermissionLiveAsync()
    {
    string token =
        Guid.NewGuid()
            .ToString("N")[..8];

    string initialTitle =
        $"{UiFixturePrefix} {token}";

    string invokedTitle =
        $"{initialTitle} — SAVE INVOKED";

    using Process fixture =
        StartUiFixtureProcess(
            token);

    try
    {
        var windows =
            new DesktopWindowTargetService();

        DesktopWindowTarget target =
            await WaitForFixtureWindowAsync(
                windows,
                fixture,
                initialTitle);

        var ui =
            new WindowsDesktopUiAutomationReader();

        var sequence =
            new List<string>();

        var invoke =
            new RecordingUiActionExecutor(
                new WindowsDesktopUiActionExecutor(),
                sequence);

        var mouse =
            new RecordingMouseActionExecutor(
                new WindowsDesktopMouseActionExecutor(),
                sequence);

        using var handler =
            new FakeHttp(
                (_, _) =>
                    throw new InvalidOperationException(
                        "Sensitive permission live test attempted Gemini."));

        using var client =
            new HttpClient(
                handler);

        var chat =
            new ChatCoordinator(
                new FakeCredentials
                {
                    Key =
                        "unused-sensitive-live-key"
                },
                new ChatSettings
                {
                    Provider =
                        ChatProvider.Gemini,

                    UseDesktopActions =
                        true,

                    DesktopPermission =
                        DesktopPermissionLevel
                            .Interaction
                },
                () => null,
                client);

        var desktopRouter =
            new LocalDesktopCommandRouter(
                new DesktopAppCatalogService(
                    () =>
                        Array.Empty<
                            DesktopAppTarget>()),
                windows);

        var intentRouter =
            new AssistantIntentRouter(
                desktopRouter);

        var action =
            new InvokeDesktopUiControlAction(
                () =>
                    chat.Options
                        .UseDesktopActions,
                windows,
                ui,
                invoke,
                mouse);

        var actions =
            new AssistantActionRouter(
                new[]
                {
                    action
                },
                () =>
                    chat.Options
                        .DesktopPermission);

        var assistant =
            new AssistantController(
                chat,
                intentRouter:
                    intentRouter,
                actions:
                    actions);

        string command =
            $"klik tombol Save di window {initialTitle}";

        AssistantReply blocked =
            await assistant.SendAsync(
                new AssistantRequest(
                    command));

        Require(
            blocked.ActionProposal is null,
            "Sensitive Save was allowed at Interaction permission.");

        Require(
            invoke.Calls == 0 &&
            mouse.Calls == 0,
            "Sensitive Save reached executor at Interaction permission.");

        Require(
            windows.Capture()
                .Any(
                    item =>
                        item.ProcessId ==
                            fixture.Id &&
                        string.Equals(
                            item.Title,
                            initialTitle,
                            StringComparison.Ordinal)),
            "Save changed fixture before Sensitive permission.");

        chat.Configure(
            chat.Options with
            {
                DesktopPermission =
                    DesktopPermissionLevel
                        .Sensitive
            });

        AssistantReply review =
            await assistant.SendAsync(
                new AssistantRequest(
                    command));

        Require(
            review.ActionProposal is
            {
                Risk:
                    AssistantActionRisk.Sensitive,

                ConfirmationStage:
                    AssistantConfirmationStage
                        .SensitiveReview
            },
            "Save did not enter SensitiveReview.");

        Require(
            invoke.Calls == 0 &&
            mouse.Calls == 0 &&
            sequence.Count == 0,
            "Sensitive Save executed before review confirmation.");

        Require(
            handler.Calls == 0,
            "Sensitive review called Gemini.");

        Guid reviewId =
            review.ActionProposal!.Id;

        AssistantReply final =
            await assistant
                .ConfirmActionAsync(
                    reviewId);

        Require(
            final.ActionProposal is
            {
                Risk:
                    AssistantActionRisk.Sensitive,

                ConfirmationStage:
                    AssistantConfirmationStage
                        .SensitiveFinal
            },
            "First confirmation did not produce SensitiveFinal.");

        Require(
            final.ActionProposal!.Id !=
                reviewId,
            "Sensitive final reused review proposal id.");

        Require(
            invoke.Calls == 0 &&
            mouse.Calls == 0 &&
            sequence.Count == 0,
            "First Sensitive confirmation reached native executor.");

        Require(
            windows.Capture().Any(item => item.ProcessId == fixture.Id &&
                string.Equals(item.Title, initialTitle, StringComparison.Ordinal)),
            "First Sensitive confirmation changed the fixture title.");

        AssistantReply executed =
            await assistant
                .ConfirmActionAsync(
                    final.ActionProposal.Id);

        Require(
            executed.ActionProposal is null,
            "Sensitive execution returned another proposal.");

        Require(
            invoke.Calls == 1,
            "Save was not invoked exactly once.");

        Require(
            mouse.Calls == 0,
            "Mouse fallback unexpectedly ran for WPF Save button.");

        Require(
            sequence.Count == 1 &&
            sequence[0] ==
                "uia",
            "Unexpected sensitive execution sequence.");

        await WaitForFixtureWindowAsync(
            windows,
            fixture,
            invokedTitle);

        Require(
            !assistant.HasPendingAction,
            "Sensitive action remained pending after execution.");

        Require(
            handler.Calls == 0,
            "Sensitive execution called Gemini.");

        Require(
            assistant.Conversation
                .GetRecentContext()
                .Count == 0,
            "Sensitive desktop action leaked into Gemini context.");

        Console.WriteLine();
        Console.WriteLine(
            "Interaction permission: Save blocked.");

        Console.WriteLine(
            "Sensitive permission: review created.");

        Console.WriteLine(
            "First Yes: no native execution.");

        Console.WriteLine(
            "Second Yes: exact Save UIA Invoke.");

        Console.WriteLine(
            "Gemini calls: 0.");

        Console.WriteLine();
        Console.WriteLine(
            "PASS: native sensitive-permission acceptance.");
    }
    finally
    {
        await StopUiFixtureAsync(
            fixture);
    }
    }

    private const string UiFixturePrefix = "LuKnight UIA Fixture";

    private const string UiTextExpectedValue =
        "Hello Lu-Knight Ω 123";

    private sealed class
        RecordingUiTextActionExecutor
            : IDesktopUiTextActionExecutor
    {
        private readonly
            IDesktopUiTextActionExecutor
            _inner;

        public int Calls;

        public string? LastValue;

        public DesktopUiTextResult?
            LastResult;

        public RecordingUiTextActionExecutor(
            IDesktopUiTextActionExecutor inner)
        {
            _inner =
                inner;
        }

        public async Task<DesktopUiTextResult>
            SetTextAsync(
                DesktopWindowTarget window,
                string controlPath,
                string expectedFingerprint,
                string value,
                CancellationToken cancellationToken = default)
        {
            Calls++;

            LastValue =
                value;

            LastResult =
                await _inner.SetTextAsync(
                    window,
                    controlPath,
                    expectedFingerprint,
                    value,
                    cancellationToken);

            return LastResult;
        }
    }

    private const string UiKeyboardExpectedValue =
        "Hello Keyboard Ω 456";

    private sealed class KeyboardOnlyEdit
        : Border
    {
        private readonly TextBlock _display;
        private readonly StringBuilder _text =
            new();

        private bool _replaceOnNextText;

        public event Action<string>? TextChanged;

        public KeyboardOnlyEdit()
        {
            Background =
                Brushes.White;

            BorderBrush =
                Brushes.Gray;

            BorderThickness =
                new Thickness(1);

            Padding =
                new Thickness(
                    8,
                    5,
                    8,
                    5);

            Focusable =
                true;

            _text.Append("Existing fixture text");

            _display =
                new TextBlock
                {
                    Text =
                        _text.ToString()
                };

            Child =
                _display;

            AutomationProperties.SetName(
                this,
                "Keyboard Only");

            AutomationProperties.SetAutomationId(
                this,
                "KeyboardOnlyEdit");
        }

        public string Text =>
            _text.ToString();

        protected override AutomationPeer
            OnCreateAutomationPeer() =>
            new KeyboardOnlyEditAutomationPeer(
                this);

        protected override void OnPreviewKeyDown(
            KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);

            if ((Keyboard.Modifiers &
                 ModifierKeys.Control) != 0 &&
                e.Key == Key.A)
            {
                _replaceOnNextText =
                    true;

                e.Handled =
                    true;
            }
        }

        protected override void OnPreviewTextInput(
            TextCompositionEventArgs e)
        {
            base.OnPreviewTextInput(e);

            if (string.IsNullOrEmpty(
                    e.Text))
            {
                return;
            }

            if (_replaceOnNextText)
            {
                _text.Clear();

                _replaceOnNextText =
                    false;
            }

            _text.Append(
                e.Text);

            _display.Text =
                _text.ToString();

            TextChanged?.Invoke(
                _text.ToString());

            e.Handled =
                true;
        }
    }

    private sealed class
        KeyboardOnlyEditAutomationPeer
            : FrameworkElementAutomationPeer
    {
        public KeyboardOnlyEditAutomationPeer(
            KeyboardOnlyEdit owner)
            : base(owner)
        {
        }

        protected override
            AutomationControlType
            GetAutomationControlTypeCore() =>
            AutomationControlType.Edit;

        protected override string
            GetClassNameCore() =>
            "KeyboardOnlyEdit";

        protected override string
            GetNameCore() =>
            AutomationProperties.GetName(
                Owner);

        protected override bool
            IsControlElementCore() =>
            true;

        protected override bool
            IsContentElementCore() =>
            true;

        protected override bool
            IsKeyboardFocusableCore() =>
            true;

        // Intentionally no ValuePattern.
        public override object GetPattern(
            PatternInterface patternInterface) =>
            null!;
    }

    private sealed class
        RecordingUiTextExecutor
            : IDesktopUiTextActionExecutor
    {
        private readonly
            IDesktopUiTextActionExecutor
            _inner;

        private readonly
            IList<string>
            _sequence;

        public int Calls;

        public DesktopUiTextResult?
            LastResult;

        public RecordingUiTextExecutor(
            IDesktopUiTextActionExecutor inner,
            IList<string> sequence)
        {
            _inner =
                inner;

            _sequence =
                sequence;
        }

        public async Task<DesktopUiTextResult>
            SetTextAsync(
                DesktopWindowTarget window,
                string controlPath,
                string expectedFingerprint,
                string value,
                CancellationToken cancellationToken = default)
        {
            Calls++;

            _sequence.Add(
                "uia");

            LastResult =
                await _inner.SetTextAsync(
                    window,
                    controlPath,
                    expectedFingerprint,
                    value,
                    cancellationToken);

            return LastResult;
        }
    }

    private sealed class
        RecordingKeyboardTextExecutor
            : IDesktopKeyboardTextActionExecutor
    {
        private readonly
            IDesktopKeyboardTextActionExecutor
            _inner;

        private readonly
            IList<string>
            _sequence;

        public int Calls;

        public DesktopActionResult?
            LastResult;

        public string? LastValue;

        public RecordingKeyboardTextExecutor(
            IDesktopKeyboardTextActionExecutor inner,
            IList<string> sequence)
        {
            _inner =
                inner;

            _sequence =
                sequence;
        }

        public async Task<DesktopActionResult>
            ReplaceTextAsync(
                DesktopWindowTarget window,
                string controlPath,
                string expectedFingerprint,
                string value,
                CancellationToken cancellationToken = default)
        {
            Calls++;

            LastValue =
                value;

            _sequence.Add(
                "keyboard");

            LastResult =
                await _inner.ReplaceTextAsync(
                    window,
                    controlPath,
                    expectedFingerprint,
                    value,
                    cancellationToken);

            return LastResult;
        }
    }

    private sealed class MouseOnlyButton
        : Border
    {
        public MouseOnlyButton()
        {
            Background =
                Brushes.Gainsboro;

            BorderBrush =
                Brushes.Gray;

            BorderThickness =
                new Thickness(1);

            CornerRadius =
                new CornerRadius(3);

            Padding =
                new Thickness(
                    12,
                    6,
                    12,
                    6);

            Focusable =
                true;

            Cursor =
                Cursors.Hand;

            Child =
                new TextBlock
                {
                    Text =
                        "Mouse Only",

                    HorizontalAlignment =
                        HorizontalAlignment.Center,

                    VerticalAlignment =
                        VerticalAlignment.Center
                };

            AutomationProperties.SetName(
                this,
                "Mouse Only");
        }

        protected override AutomationPeer
            OnCreateAutomationPeer() =>
            new MouseOnlyButtonAutomationPeer(
                this);
    }

    private sealed class
        MouseOnlyButtonAutomationPeer
            : FrameworkElementAutomationPeer
    {
        public MouseOnlyButtonAutomationPeer(
            MouseOnlyButton owner)
            : base(owner)
        {
        }

        protected override
            AutomationControlType
            GetAutomationControlTypeCore() =>
            AutomationControlType.Button;

        protected override string
            GetClassNameCore() =>
            "MouseOnlyButton";

        protected override string
            GetNameCore()
        {
            string name =
                AutomationProperties.GetName(
                    Owner);

            return string.IsNullOrWhiteSpace(
                    name)
                ? "Mouse Only"
                : name;
        }

        protected override bool
            IsControlElementCore() =>
            true;

        protected override bool
            IsContentElementCore() =>
            true;

        // Intentionally expose NO UI Automation
        // action patterns.
        public override object GetPattern(
            PatternInterface patternInterface) =>
            null!;
    }

    private sealed class
        RecordingUiActionExecutor
            : IDesktopUiActionExecutor
    {
        private readonly
            IDesktopUiActionExecutor
            _inner;

        private readonly
            IList<string>
            _sequence;

        public int Calls;

        public DesktopUiInvokeResult?
            LastResult;

        public RecordingUiActionExecutor(
            IDesktopUiActionExecutor inner,
            IList<string> sequence)
        {
            _inner =
                inner;

            _sequence =
                sequence;
        }

        public async Task<DesktopUiInvokeResult>
            InvokeAsync(
                DesktopWindowTarget window,
                string controlPath,
                string expectedFingerprint,
                CancellationToken cancellationToken = default)
        {
            Calls++;

            _sequence.Add(
                "uia");

            DesktopUiInvokeResult result =
                await _inner.InvokeAsync(
                    window,
                    controlPath,
                    expectedFingerprint,
                    cancellationToken);

            LastResult =
                result;

            return result;
        }
    }

    private sealed class
        RecordingMouseActionExecutor
            : IDesktopMouseActionExecutor
    {
        private readonly
            IDesktopMouseActionExecutor
            _inner;

        private readonly
            IList<string>
            _sequence;

        public int Calls;

        public DesktopActionResult? LastResult;

        public RecordingMouseActionExecutor(
            IDesktopMouseActionExecutor inner,
            IList<string> sequence)
        {
            _inner =
                inner;

            _sequence =
                sequence;
        }

        public async Task<DesktopActionResult>
            ClickAsync(
                DesktopWindowTarget window,
                string controlPath,
                string expectedFingerprint,
                CancellationToken cancellationToken = default)
        {
            Calls++;

            _sequence.Add(
                "mouse");

            LastResult = await _inner.ClickAsync(
                window,
                controlPath,
                expectedFingerprint,
                cancellationToken);

            return LastResult;
        }
    }

    private sealed class
        RecordingScreenEvidenceService
            : IDesktopUiScreenEvidenceService
    {
        private readonly
            IDesktopUiScreenEvidenceService
            _inner;

        public int Calls;

        public Dictionary<
            string,
            DesktopUiScreenEvidenceResult>
            Results { get; } =
            new(
                StringComparer.Ordinal);

        public RecordingScreenEvidenceService(
            IDesktopUiScreenEvidenceService inner)
        {
            _inner =
                inner;
        }

        public async Task<
            DesktopUiScreenEvidenceResult>
            CaptureAsync(
                DesktopWindowTarget window,
                string controlPath,
                string expectedFingerprint,
                CancellationToken cancellationToken = default)
        {
            Calls++;

            DesktopUiScreenEvidenceResult result =
                await _inner.CaptureAsync(
                    window,
                    controlPath,
                    expectedFingerprint,
                    cancellationToken);

            Results[controlPath] =
                result;

            return result;
        }
    }

    private static void RunUiActionFixtureHost(string token)
    {
        string initialTitle = $"{UiFixturePrefix} {token}";
        var window = new Window
        {
            Title = initialTitle,
            Width = 420,
            Height = 680,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            ShowInTaskbar = true,
            Topmost = true
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        var description = new TextBlock
        {
            Text = "LuKnight UI Automation acceptance fixture",
            Margin = new Thickness(0, 0, 0, 16)
        };
        var refresh = new Button
        {
            Content = "Refresh",
            Width = 140,
            Height = 40,
            Margin = new Thickness(0, 0, 0, 12)
        };
        AutomationProperties.SetName(refresh, "Refresh");
        var save = new Button { Content = "Save", Width = 140, Height = 40 };
        AutomationProperties.SetName(save, "Save");
        var mouseTarget = new Button
        {
            Content = "Mouse Target",
            Width = 140,
            Height = 40,
            Margin = new Thickness(0, 12, 0, 0)
        };
        AutomationProperties.SetName(mouseTarget, "Mouse Target");

        var mouseOnly =
            new MouseOnlyButton
            {
                Width =
                    140,

                Height =
                    40,

                Margin =
                    new Thickness(
                        0,
                        12,
                        0,
                        0)
            };

        mouseOnly.MouseLeftButtonUp +=
            (_, e) =>
            {
                e.Handled =
                    true;

                window.Title =
                    $"{initialTitle} — MOUSE FALLBACK CLICKED";
            };

        var assistGrid =
            new Grid
            {
                Width =
                    140,

                Height =
                    40,

                Margin =
                    new Thickness(
                        0,
                        12,
                        0,
                        0)
            };

        var assistBottom =
            new Button
            {
                Content =
                    "Assist Duplicate"
            };

        AutomationProperties.SetName(
            assistBottom,
            "Assist Duplicate");

        AutomationProperties.SetAutomationId(
            assistBottom,
            "AssistBottom");


        var assistTop =
            new Button
            {
                Content =
                    "Assist Duplicate"
            };

        AutomationProperties.SetName(
            assistTop,
            "Assist Duplicate");

        AutomationProperties.SetAutomationId(
            assistTop,
            "AssistTop");

        assistBottom.Click +=
            (_, _) =>
            {
                window.Title =
                    $"{initialTitle} — ASSIST BOTTOM INVOKED";
            };

        assistTop.Click +=
            (_, _) =>
            {
                window.Title =
                    $"{initialTitle} — ASSIST TOP INVOKED";
            };

        assistGrid.Children.Add(
            assistBottom);

        assistGrid.Children.Add(
            assistTop);

        var searchBox =
            new TextBox
            {
                Width = 220,
                Height = 32,
                Margin =
                    new Thickness(
                        0,
                        12,
                        0,
                        0)
            };

        AutomationProperties.SetName(
            searchBox,
            "Search");

        AutomationProperties.SetAutomationId(
            searchBox,
            "SearchBox");


        var apiKeyBox =
            new TextBox
            {
                Width = 220,
                Height = 32,
                Margin =
                    new Thickness(
                        0,
                        12,
                        0,
                        0)
            };

        AutomationProperties.SetName(
            apiKeyBox,
            "API Key");

        AutomationProperties.SetAutomationId(
            apiKeyBox,
            "ApiKeyInput");


        var passwordBox =
            new PasswordBox
            {
                Width = 220,
                Height = 32,
                Margin =
                    new Thickness(
                        0,
                        12,
                        0,
                        0)
            };

        AutomationProperties.SetName(
            passwordBox,
            "Password");

        AutomationProperties.SetAutomationId(
            passwordBox,
            "PasswordBox");

        var keyboardOnly =
            new KeyboardOnlyEdit
            {
                Width =
                    220,

                Height =
                    32,

                Margin =
                    new Thickness(
                        0,
                        12,
                        0,
                        0)
            };

        keyboardOnly.TextChanged +=
            text =>
            {
                if (text ==
                    UiKeyboardExpectedValue)
                {
                    window.Title =
                        $"{initialTitle} — KEYBOARD FALLBACK SET";
                }
            };

        searchBox.TextChanged +=
            (_, _) =>
            {
                if (searchBox.Text ==
                    UiTextExpectedValue)
                {
                    window.Title =
                        $"{initialTitle} — TEXT SET";
                }
            };

        refresh.Click += (_, _) => window.Title = $"{initialTitle} — REFRESH INVOKED";
        save.Click += (_, _) => window.Title = $"{initialTitle} — SAVE INVOKED";
        mouseTarget.Click += (_, _) => window.Title = $"{initialTitle} — MOUSE CLICKED";
        panel.Children.Add(description);
        panel.Children.Add(refresh);
        panel.Children.Add(save);
        panel.Children.Add(mouseTarget);
        panel.Children.Add(mouseOnly);
        panel.Children.Add(assistGrid);
        panel.Children.Add(
            searchBox);

        panel.Children.Add(
            apiKeyBox);

        panel.Children.Add(
            passwordBox);
        panel.Children.Add(keyboardOnly);

        window.Content = panel;
        window.Loaded += (_, _) => window.Activate();

        var application = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        application.Run(window);
    }

    private static async Task CheckUiButtonInvokeLiveAsync()
    {
        string token = Guid.NewGuid().ToString("N")[..8];
        string initialTitle = $"{UiFixturePrefix} {token}";
        string invokedTitle = $"{initialTitle} — REFRESH INVOKED";
        using Process fixture = StartUiFixtureProcess(token);

        try
        {
            var windows = new DesktopWindowTargetService();
            DesktopWindowTarget target = await WaitForFixtureWindowAsync(
                windows,
                fixture,
                initialTitle);
            Console.WriteLine();
            Console.WriteLine($"Fixture PID: {fixture.Id}");
            Console.WriteLine($"Fixture window: {target.DisplayLabel}");

            var ui = new WindowsDesktopUiAutomationReader();
            DesktopUiSnapshot snapshot = await ui.CaptureAsync(target);
            Require(snapshot.Success, snapshot.Error ?? "Native UIA fixture capture failed.");

            DesktopUiControlResolution refresh =
                DesktopUiControlResolver.Resolve(snapshot, "Refresh", "Button");
            Require(refresh.Match is not null, "Refresh button was not visible through native UIA.");
            DesktopUiControlResolution save =
                DesktopUiControlResolver.Resolve(snapshot, "Save", "Button");
            Require(save.Match is not null, "Save button was not visible through native UIA.");
            Require(
                !DesktopUiActionPolicy.IsTemporarilyBlocked(refresh.Match!, out _),
                "Safe Refresh fixture button was blocked.");
            Require(
                DesktopUiActionRiskClassifier.ClassifyButton(save.Match!).Risk == AssistantActionRisk.Sensitive,
                "Sensitive Save fixture button escaped policy.");

            using var handler = new FakeHttp((_, _) =>
                throw new InvalidOperationException("Live UIA action attempted Gemini."));
            using var client = new HttpClient(handler);
            var chat = new ChatCoordinator(
                new FakeCredentials { Key = "unused-uia-live-key" },
                new ChatSettings
                {
                    Provider = ChatProvider.Gemini,
                    UseDesktopActions = true
                },
                () => null,
                client);
            var desktopRouter = new LocalDesktopCommandRouter(
                new DesktopAppCatalogService(() => Array.Empty<DesktopAppTarget>()),
                windows);
            var router = new AssistantIntentRouter(desktopRouter);
            var executor = new WindowsDesktopUiActionExecutor();
            var action = new InvokeDesktopUiControlAction(() => true, windows, ui, executor);
            var actions = new AssistantActionRouter(new IAssistantAction[] { action });
            var assistant = new AssistantController(chat, intentRouter: router, actions: actions);

            AssistantReply proposal = await assistant.SendAsync(
                new AssistantRequest($"klik tombol Refresh di window {initialTitle}"));
            Require(proposal.Backend == AssistantBackend.Local, "Live UIA preparation was not local.");
            Require(proposal.ActionProposal is not null, "Safe Refresh button did not produce confirmation.");
            Require(handler.Calls == 0, "Live UIA preparation called Gemini.");
            Require(
                assistant.Conversation.GetRecentContext().Count == 0,
                "Live UIA preparation leaked into Gemini context.");
            Require(
                windows.Capture().Any(x => x.ProcessId == fixture.Id && x.Title == initialTitle),
                "Refresh executed before confirmation.");
            Console.WriteLine("Confirmation gate passed; Refresh has not executed yet.");

            AssistantReply confirmed = await assistant.ConfirmActionAsync(proposal.ActionProposal!.Id);
            Require(confirmed.Backend == AssistantBackend.Local, "Confirmed live UIA action was not local.");
            Require(handler.Calls == 0, "Confirmed live UIA action called Gemini.");
            await WaitForFixtureWindowAsync(windows, fixture, invokedTitle);
            Console.WriteLine("Native InvokePattern successfully activated Refresh.");
            Require(
                assistant.Conversation.GetRecentContext().Count == 0,
                "Live UIA result leaked into Gemini context.");

            AssistantReply blocked = await assistant.SendAsync(
                new AssistantRequest($"klik tombol Save di window {invokedTitle}"));
            Require(
                blocked.Backend == AssistantBackend.Local && blocked.ActionProposal is null,
                "Sensitive Save button escaped policy.");
            Require(handler.Calls == 0, "Blocked Save action called Gemini.");
            await Task.Delay(300);
            Require(
                windows.Capture().Any(x => x.ProcessId == fixture.Id && x.Title == invokedTitle),
                "Blocked Save button was invoked.");
            Console.WriteLine("Sensitive Save button was correctly blocked.");
            Console.WriteLine();
            Console.WriteLine("PASS: native cross-process UIA Button Invoke acceptance.");
        }
        finally
        {
            await StopUiFixtureAsync(fixture);
        }
    }

    private static Process StartUiFixtureProcess(string token)
    {
        string processPath = Environment.ProcessPath ??
            throw new InvalidOperationException("Current process path unavailable.");
        var info = new ProcessStartInfo { FileName = processPath, UseShellExecute = false };
        if (string.Equals(
                Path.GetFileNameWithoutExtension(processPath),
                "dotnet",
                StringComparison.OrdinalIgnoreCase))
        {
            string assemblyPath = Assembly.GetEntryAssembly()?.Location ??
                throw new InvalidOperationException("Entry assembly path unavailable.");
            info.ArgumentList.Add(assemblyPath);
        }

        info.ArgumentList.Add("--uia-action-fixture-host");
        info.ArgumentList.Add($"--uia-fixture-token={token}");
        return Process.Start(info) ??
            throw new InvalidOperationException("Failed to start UIA fixture process.");
    }

    private static async Task CheckSafeMouseLiveAsync()
    {
        string token = Guid.NewGuid().ToString("N")[..8];
        string initialTitle = $"{UiFixturePrefix} {token}";
        string clickedTitle = $"{initialTitle} — MOUSE CLICKED";
        using Process fixture = StartUiFixtureProcess(token);

        try
        {
            var windows = new DesktopWindowTargetService();
            DesktopWindowTarget target = await WaitForFixtureWindowAsync(
                windows,
                fixture,
                initialTitle);
            var ui = new WindowsDesktopUiAutomationReader();
            DesktopUiSnapshot snapshot = await ui.CaptureAsync(target);
            Require(snapshot.Success, snapshot.Error ?? "Mouse fixture UIA capture failed.");

            DesktopUiControlResolution control =
                DesktopUiControlResolver.Resolve(snapshot, "Mouse Target", "Button");
            Require(control.Match is not null, "Mouse Target button was not resolved.");
            DesktopUiNodeSnapshot button = control.Match!;
            Require(
                !DesktopUiActionPolicy.IsTemporarilyBlocked(button, out _),
                "Safe mouse fixture button was blocked.");

            var mouse = new WindowsDesktopMouseActionExecutor();
            DesktopActionResult result = await mouse.ClickAsync(
                target,
                button.Path,
                DesktopUiNodeIdentity.Fingerprint(button));
            Require(result.Success, result.Message);
            DesktopWindowTarget clickedWindow = await WaitForFixtureWindowAsync(
                windows,
                fixture,
                clickedTitle);

            DesktopUiSnapshot afterClickSnapshot = await ui.CaptureAsync(clickedWindow);
            Require(
                afterClickSnapshot.Success,
                afterClickSnapshot.Error ?? "Mouse fixture recapture failed.");
            DesktopUiControlResolution save = DesktopUiControlResolver.Resolve(
                afterClickSnapshot,
                "Save",
                "Button");
            Require(save.Match is not null, "Save button disappeared from mouse fixture.");
            DesktopActionResult blockedSave = await mouse.ClickAsync(
                clickedWindow,
                save.Match!.Path,
                DesktopUiNodeIdentity.Fingerprint(save.Match));
            Require(!blockedSave.Success, "Safe mouse bypassed sensitive-button policy.");
            await Task.Delay(250);
            Require(
                windows.Capture().Any(x =>
                    x.ProcessId == fixture.Id && x.Title == clickedTitle),
                "Blocked Save button was clicked by mouse fallback.");

            Console.WriteLine();
            Console.WriteLine("Native bounded mouse click activated Mouse Target.");
            Console.WriteLine("Cursor restoration was attempted only if the user had not moved it.");
            Console.WriteLine("PASS: safe mouse primitive live acceptance.");
        }
        finally
        {
            await StopUiFixtureAsync(fixture);
        }
    }

    private static async Task
        CheckAssistantMouseFallbackLiveAsync()
    {
        string token =
            Guid.NewGuid()
                .ToString("N")[..8];

        string initialTitle =
            $"{UiFixturePrefix} {token}";

        string clickedTitle =
            $"{initialTitle} — MOUSE FALLBACK CLICKED";

        using Process fixture =
            StartUiFixtureProcess(
                token);

        try
        {
            var windows =
                new DesktopWindowTargetService();

            DesktopWindowTarget target =
                await WaitForFixtureWindowAsync(
                    windows,
                    fixture,
                    initialTitle);

            var ui =
                new WindowsDesktopUiAutomationReader();

            DesktopUiSnapshot snapshot =
                await ui.CaptureAsync(
                    target);

            Require(
                snapshot.Success,
                snapshot.Error ??
                "Mouse fallback fixture capture failed.");

            DesktopUiControlResolution
                resolved =
                    DesktopUiControlResolver.Resolve(
                        snapshot,
                        "Mouse Only",
                        "Button");

            Require(
                resolved.Match is not null,
                "Mouse Only control was not exposed as a UIA Button.");

            DesktopUiNodeSnapshot button =
                resolved.Match!;

            Require(
                button.ControlType ==
                    "Button",
                "Mouse Only control has wrong UIA ControlType.");

            Require(
                !DesktopUiActionPolicy
                    .IsTemporarilyBlocked(
                        button,
                        out _),
                "Mouse Only control was rejected by policy.");

            AutomationElement root =
                AutomationElement.FromHandle(
                    target.Handle);

            AutomationElement? nativeControl =
                DesktopUiAutomationLocator
                    .ResolvePath(
                        root,
                        button.Path);

            Require(
                nativeControl is not null,
                "Mouse Only native UIA element was not resolved.");

            bool supportsInvoke =
                nativeControl!
                    .TryGetCurrentPattern(
                        InvokePattern.Pattern,
                        out object? pattern) &&
                pattern is
                    InvokePattern;

            Require(
                !supportsInvoke,
                "Mouse Only fixture unexpectedly exposes InvokePattern.");

            Console.WriteLine();
            Console.WriteLine(
                "Fixture Mouse Only control exposes Button but no InvokePattern.");

            using var handler =
                new FakeHttp(
                    (_, _) =>
                        throw new InvalidOperationException(
                            "Mouse fallback live test attempted Gemini."));

            using var client =
                new HttpClient(
                    handler);

            var chat =
                new ChatCoordinator(
                    new FakeCredentials
                    {
                        Key =
                            "unused-mouse-fallback-key"
                    },
                    new ChatSettings
                    {
                        Provider =
                            ChatProvider.Gemini,

                        UseDesktopActions =
                            true
                    },
                    () => null,
                    client);

            var desktopRouter =
                new LocalDesktopCommandRouter(
                    new DesktopAppCatalogService(
                        () =>
                            Array.Empty<
                                DesktopAppTarget>()),
                    windows);

            var router =
                new AssistantIntentRouter(
                    desktopRouter);

            var sequence =
                new List<string>();

            var uiExecutor =
                new RecordingUiActionExecutor(
                    new WindowsDesktopUiActionExecutor(),
                    sequence);

            var mouseExecutor =
                new RecordingMouseActionExecutor(
                    new WindowsDesktopMouseActionExecutor(),
                    sequence);

            var action =
                new InvokeDesktopUiControlAction(
                    () => true,
                    windows,
                    ui,
                    uiExecutor,
                    mouseExecutor);

            var actions =
                new AssistantActionRouter(
                    new IAssistantAction[]
                    {
                        action
                    });

            var assistant =
                new AssistantController(
                    chat,
                    intentRouter: router,
                    actions: actions);

            AssistantReply proposal =
                await assistant.SendAsync(
                    new AssistantRequest(
                        $"klik tombol Mouse Only di window {initialTitle}"));

            Require(
                proposal.Backend ==
                    AssistantBackend.Local,
                "Mouse fallback preparation was not local.");

            Require(
                proposal.ActionProposal
                    is not null,
                "Mouse-only button did not produce confirmation.");

            Require(
                proposal.ActionProposal!
                    .ConfirmationText
                    .Contains(
                        "mouse",
                        StringComparison.OrdinalIgnoreCase),
                "Mouse fallback was not disclosed to user.");

            Require(
                handler.Calls == 0,
                "Mouse fallback preparation called Gemini.");

            Require(
                uiExecutor.Calls == 0 &&
                mouseExecutor.Calls == 0,
                "Execution occurred before confirmation.");

            Require(
                sequence.Count == 0,
                "Backend execution occurred before confirmation.");

            Require(
                windows.Capture()
                    .Any(
                        x =>
                            x.ProcessId ==
                                fixture.Id &&
                            x.Title ==
                                initialTitle),
                "Mouse-only control was clicked before confirmation.");

            Require(
                assistant.Conversation
                    .GetRecentContext()
                    .Count == 0,
                "Mouse fallback preparation leaked into Gemini context.");

            Console.WriteLine(
                "Confirmation gate passed; no UIA or mouse action executed yet.");

            AssistantReply confirmed =
                await assistant
                    .ConfirmActionAsync(
                        proposal.ActionProposal.Id);

            Require(
                confirmed.Backend ==
                    AssistantBackend.Local,
                "Mouse fallback execution was not local.");

            Require(
                handler.Calls == 0,
                "Mouse fallback execution called Gemini.");

            Require(
                uiExecutor.Calls == 1,
                "UI Automation was not attempted exactly once.");

            Require(
                uiExecutor.LastResult?.Outcome ==
                    DesktopUiInvokeOutcome.UnsupportedPattern,
                "UIA did not report UnsupportedPattern.");

            Require(
                mouseExecutor.Calls == 1,
                "Mouse fallback was not used exactly once.");

            Require(
                sequence.Count == 2 &&
                sequence[0] == "uia" &&
                sequence[1] == "mouse",
                "Mouse fallback did not occur strictly after UIA.");

            Require(
                mouseExecutor.LastResult?.Success == true,
                mouseExecutor.LastResult?.Message ?? "Mouse fallback returned no result.");

            await WaitForFixtureWindowAsync(
                windows,
                fixture,
                clickedTitle);

            Require(
                confirmed.Text.Contains(
                    "mouse",
                    StringComparison.OrdinalIgnoreCase),
                "Assistant result did not disclose mouse fallback.");

            Require(
                assistant.Conversation
                    .GetRecentContext()
                    .Count == 0,
                "Mouse fallback result leaked into Gemini context.");

            Console.WriteLine(
                "Execution order: UIA → mouse.");

            Console.WriteLine(
                "Native mouse fallback activated Mouse Only.");

            Console.WriteLine();
            Console.WriteLine(
                "PASS: Assistant UIA-first mouse fallback acceptance.");
        }
        finally
        {
            await StopUiFixtureAsync(
                fixture);
        }
    }

    private static async Task
        CheckUiTextLiveAsync()
    {
        string token =
            Guid.NewGuid()
                .ToString("N")[..8];

        string initialTitle =
            $"{UiFixturePrefix} {token}";

        string changedTitle =
            $"{initialTitle} — TEXT SET";

        using Process fixture =
            StartUiFixtureProcess(
                token);

        try
        {
            var windows =
                new DesktopWindowTargetService();

            DesktopWindowTarget window =
                await WaitForFixtureWindowAsync(
                    windows,
                    fixture,
                    initialTitle);

            var ui =
                new WindowsDesktopUiAutomationReader();

            DesktopUiSnapshot snapshot =
                await ui.CaptureAsync(
                    window);

            Require(
                snapshot.Success,
                snapshot.Error ??
                "UI text fixture capture failed.");

            DesktopUiControlResolution search =
                DesktopUiControlResolver.Resolve(
                    snapshot,
                    "Search",
                    "Edit");

            Require(
                search.Match is not null,
                "Search TextBox was not visible through UIA.");

            DesktopUiNodeSnapshot field =
                search.Match!;

            Require(
                DesktopUiTextInputPolicy
                    .ValidateTarget(
                        field,
                        out _),
                "Search TextBox was rejected by policy.");

            DesktopUiControlResolution apiKey = DesktopUiControlResolver.Resolve(snapshot, "API Key", "Edit");
            Require(apiKey.Match is not null &&
                !DesktopUiTextInputPolicy.ValidateTarget(apiKey.Match, out _),
                "API Key fixture field was missing or not blocked by policy.");
            Require(snapshot.Nodes.Any(node => node.IsPassword && node.IsProtected),
                "Password fixture field was not exposed as protected through UIA.");

            AutomationElement root =
                AutomationElement.FromHandle(
                    window.Handle);

            AutomationElement? nativeField =
                DesktopUiAutomationLocator
                    .ResolvePath(
                        root,
                        field.Path);

            Require(
                nativeField is not null,
                "Native Search TextBox was not resolved.");

            bool supportsValue =
                nativeField!
                    .TryGetCurrentPattern(
                        ValuePattern.Pattern,
                        out object? rawPattern) &&
                rawPattern is
                    ValuePattern;

            Require(
                supportsValue,
                "Search TextBox does not expose ValuePattern.");

            var valuePattern =
                (ValuePattern)rawPattern!;

            Require(
                !valuePattern.Current.IsReadOnly,
                "Search TextBox unexpectedly became read-only.");

            using var handler =
                new FakeHttp(
                    (_, _) =>
                        throw new InvalidOperationException(
                            "UI text live test attempted Gemini."));

            using var client =
                new HttpClient(
                    handler);

            var chat =
                new ChatCoordinator(
                    new FakeCredentials
                    {
                        Key =
                            "unused-ui-text-live-key"
                    },
                    new ChatSettings
                    {
                        Provider =
                            ChatProvider.Gemini,

                        UseDesktopActions =
                            true
                    },
                    () => null,
                    client);

            var desktopRouter =
                new LocalDesktopCommandRouter(
                    new DesktopAppCatalogService(
                        () =>
                            Array.Empty<
                                DesktopAppTarget>()),
                    windows);

            var router =
                new AssistantIntentRouter(
                    desktopRouter);

            var textExecutor =
                new RecordingUiTextActionExecutor(
                    new WindowsDesktopUiTextActionExecutor());

            var textAction =
                new SetDesktopUiTextAction(
                    () => true,
                    windows,
                    ui,
                    textExecutor);

            var assistant =
                new AssistantController(
                    chat,
                    intentRouter:
                        router,
                    actions:
                        new AssistantActionRouter(
                            new IAssistantAction[]
                            {
                                textAction
                            }));

            string command =
                $"isi textbox Search dengan {UiTextExpectedValue} di window {initialTitle}";

            AssistantReply proposal =
                await assistant.SendAsync(
                    new AssistantRequest(
                        command));

            Require(
                proposal.Backend ==
                    AssistantBackend.Local,
                "UI text preparation was not local.");

            Require(
                proposal.ActionProposal
                    is not null,
                "UI text input did not request confirmation.");

            Require(
                textExecutor.Calls == 0,
                "Text changed before confirmation.");

            Require(
                handler.Calls == 0,
                "UI text preparation called Gemini.");

            Require(
                !proposal.ActionProposal!
                    .ConfirmationText
                    .Contains(
                        UiTextExpectedValue,
                        StringComparison.Ordinal),
                "Confirmation exposed text content.");

            Require(
                proposal.ActionProposal
                    .ConfirmationText
                    .Contains(
                        $"{UiTextExpectedValue.Length} karakter",
                        StringComparison.Ordinal),
                "Confirmation did not disclose text length.");

            Require(
                windows.Capture()
                    .Any(
                        x =>
                            x.ProcessId ==
                                fixture.Id &&
                            x.Title ==
                                initialTitle),
                "TextBox changed before confirmation.");

            Require(
                assistant.Conversation
                    .GetRecentContext()
                    .Count == 0,
                "UI text preparation leaked into Gemini context.");

            AssistantReply confirmed =
                await assistant
                    .ConfirmActionAsync(
                        proposal.ActionProposal.Id);

            Require(
                confirmed.Backend ==
                    AssistantBackend.Local,
                "UI text execution was not local.");

            Require(
                textExecutor.Calls == 1,
                "UI text executor was not called exactly once.");

            Require(
                textExecutor.LastValue ==
                    UiTextExpectedValue,
                "Exact text changed before SetValue.");

            Require(
                textExecutor.LastResult?
                    .Success == true,
                textExecutor.LastResult?
                    .Message ??
                "UI text executor returned no result.");

            Require(
                handler.Calls == 0,
                "UI text execution called Gemini.");

            await WaitForFixtureWindowAsync(
                windows,
                fixture,
                changedTitle);

            Require(
                assistant.Conversation
                    .GetRecentContext()
                    .Count == 0,
                "UI text result leaked into Gemini context.");

            Console.WriteLine();
            Console.WriteLine(
                "Native ValuePattern.SetValue activated Search.");

            Console.WriteLine(
                "Exact Unicode text reached the TextBox.");

            Console.WriteLine(
                "No Gemini/context leak occurred.");

            int callsBeforeSensitive =
                textExecutor.Calls;

            AssistantReply blockedApi =
                await assistant.SendAsync(
                    new AssistantRequest(
                        $"isi textbox API Key dengan harmless-test-value di window {changedTitle}"));

            Require(
                blockedApi.Backend ==
                    AssistantBackend.Local &&
                blockedApi.ActionProposal
                    is null,
                "API Key field produced an executable proposal.");

            Require(
                textExecutor.Calls ==
                    callsBeforeSensitive,
                "API Key field reached text executor.");

            Require(
                handler.Calls == 0,
                "API Key rejection reached Gemini.");

            AssistantReply blockedPassword =
                await assistant.SendAsync(
                    new AssistantRequest(
                        $"isi textbox Password dengan harmless-test-value di window {changedTitle}"));

            Require(
                blockedPassword.Backend ==
                    AssistantBackend.Local &&
                blockedPassword.ActionProposal
                    is null,
                "Password field produced an executable proposal.");

            Require(
                textExecutor.Calls ==
                    callsBeforeSensitive,
                "Password field reached text executor.");

            Require(
                handler.Calls == 0,
                "Password rejection reached Gemini.");

            Require(
                assistant.Conversation
                    .GetRecentContext()
                    .Count == 0,
                "Sensitive text request leaked into context.");

            Console.WriteLine(
                "API Key and Password fields were blocked.");

            Console.WriteLine();
            Console.WriteLine(
                "PASS: native Assistant UIA ValuePattern text acceptance.");
        }
        finally
        {
            await StopUiFixtureAsync(
                fixture);
        }
    }

    private static async Task
        CheckKeyboardFallbackLiveAsync()
    {
        string token =
            Guid.NewGuid()
                .ToString("N")[..8];

        string initialTitle =
            $"{UiFixturePrefix} {token}";

        string changedTitle =
            $"{initialTitle} — KEYBOARD FALLBACK SET";

        using Process fixture =
            StartUiFixtureProcess(
                token);

        try
        {
            var windows =
                new DesktopWindowTargetService();

            DesktopWindowTarget window =
                await WaitForFixtureWindowAsync(
                    windows,
                    fixture,
                    initialTitle);

            var ui =
                new WindowsDesktopUiAutomationReader();

            DesktopUiSnapshot snapshot =
                await ui.CaptureAsync(
                    window);

            Require(
                snapshot.Success,
                snapshot.Error ??
                "Keyboard fallback fixture capture failed.");

            DesktopUiControlResolution resolved =
                DesktopUiControlResolver.Resolve(
                    snapshot,
                    "Keyboard Only",
                    "Edit");

            Require(
                resolved.Match is not null,
                "Keyboard Only control was not exposed as Edit.");

            DesktopUiNodeSnapshot field =
                resolved.Match!;

            Require(
                DesktopUiTextInputPolicy
                    .ValidateTarget(
                        field,
                        out _),
                "Keyboard Only field was rejected by policy.");

            AutomationElement root =
                AutomationElement.FromHandle(
                    window.Handle);

            AutomationElement? nativeField =
                DesktopUiAutomationLocator
                    .ResolvePath(
                        root,
                        field.Path);

            Require(
                nativeField is not null,
                "Native Keyboard Only field was not resolved.");

            Require(
                nativeField!
                    .Current
                    .IsKeyboardFocusable,
                "Keyboard Only field is not keyboard focusable.");

            bool hasValuePattern =
                nativeField.TryGetCurrentPattern(
                    ValuePattern.Pattern,
                    out object? pattern) &&
                pattern is ValuePattern;

            Require(
                !hasValuePattern,
                "Keyboard Only fixture unexpectedly exposes ValuePattern.");

            Console.WriteLine();
            Console.WriteLine(
                "Keyboard Only is Edit + focusable + no ValuePattern.");

            using var handler =
                new FakeHttp(
                    (_, _) =>
                        throw new InvalidOperationException(
                            "Keyboard fallback live test attempted Gemini."));

            using var client =
                new HttpClient(
                    handler);

            var chat =
                new ChatCoordinator(
                    new FakeCredentials
                    {
                        Key =
                            "unused-keyboard-live-key"
                    },
                    new ChatSettings
                    {
                        Provider =
                            ChatProvider.Gemini,

                        UseDesktopActions =
                            true
                    },
                    () => null,
                    client);

            var desktopRouter =
                new LocalDesktopCommandRouter(
                    new DesktopAppCatalogService(
                        () =>
                            Array.Empty<
                                DesktopAppTarget>()),
                    windows);

            var router =
                new AssistantIntentRouter(
                    desktopRouter);

            var sequence =
                new List<string>();

            var uiTextExecutor =
                new RecordingUiTextExecutor(
                    new WindowsDesktopUiTextActionExecutor(),
                    sequence);

            var keyboardExecutor =
                new RecordingKeyboardTextExecutor(
                    new WindowsDesktopKeyboardTextActionExecutor(),
                    sequence);

            var action =
                new SetDesktopUiTextAction(
                    () => true,
                    windows,
                    ui,
                    uiTextExecutor,
                    keyboardExecutor);

            var assistant =
                new AssistantController(
                    chat,
                    intentRouter:
                        router,
                    actions:
                        new AssistantActionRouter(
                            new IAssistantAction[]
                            {
                                action
                            }));

            string command =
                $"isi textbox Keyboard Only dengan {UiKeyboardExpectedValue} di window {initialTitle}";

            AssistantReply proposal =
                await assistant.SendAsync(
                    new AssistantRequest(
                        command));

            Require(
                proposal.Backend ==
                    AssistantBackend.Local,
                "Keyboard fallback preparation was not local.");

            Require(
                proposal.ActionProposal
                    is not null,
                "Keyboard fallback did not request confirmation.");

            Require(
                uiTextExecutor.Calls == 0 &&
                keyboardExecutor.Calls == 0 &&
                sequence.Count == 0,
                "Text execution occurred before confirmation.");

            Require(
                handler.Calls == 0,
                "Keyboard fallback preparation called Gemini.");

            Require(
                proposal.ActionProposal!
                    .ConfirmationText
                    .Contains(
                        "keyboard",
                        StringComparison.OrdinalIgnoreCase),
                "Keyboard fallback was not disclosed.");

            Require(
                !proposal.ActionProposal
                    .ConfirmationText
                    .Contains(
                        UiKeyboardExpectedValue,
                        StringComparison.Ordinal),
                "Confirmation exposed text content.");

            Require(
                windows.Capture()
                    .Any(
                        x =>
                            x.ProcessId ==
                                fixture.Id &&
                            x.Title ==
                                initialTitle),
                "Keyboard fallback executed before confirmation.");

            Require(
                assistant.Conversation
                    .GetRecentContext()
                    .Count == 0,
                "Keyboard fallback preparation leaked into context.");

            AssistantReply confirmed =
                await assistant
                    .ConfirmActionAsync(
                        proposal.ActionProposal.Id);

            Require(
                confirmed.Backend ==
                    AssistantBackend.Local,
                "Keyboard fallback execution was not local.");

            Require(
                handler.Calls == 0,
                "Keyboard fallback called Gemini.");

            Require(
                uiTextExecutor.Calls == 1,
                "ValuePattern executor was not attempted exactly once.");

            Require(
                uiTextExecutor.LastResult?
                    .Outcome ==
                    DesktopUiTextOutcome.UnsupportedPattern,
                "ValuePattern executor did not report UnsupportedPattern.");

            Require(
                keyboardExecutor.Calls == 1,
                "Keyboard fallback was not executed exactly once.");

            Require(
                keyboardExecutor.LastValue ==
                    UiKeyboardExpectedValue,
                "Keyboard fallback changed exact input value.");

            Require(
                sequence.Count == 2 &&
                sequence[0] ==
                    "uia" &&
                sequence[1] ==
                    "keyboard",
                "Keyboard fallback did not occur strictly after UIA.");

            Require(
                keyboardExecutor.LastResult?
                    .Success == true,
                keyboardExecutor.LastResult?
                    .Message ??
                "Keyboard executor returned no result.");

            await WaitForFixtureWindowAsync(
                windows,
                fixture,
                changedTitle);

            Require(
                confirmed.Text.Contains(
                    "keyboard",
                    StringComparison.OrdinalIgnoreCase),
                "Assistant result did not disclose keyboard fallback.");

            Require(
                assistant.Conversation
                    .GetRecentContext()
                    .Count == 0,
                "Keyboard fallback result leaked into context.");

            Console.WriteLine(
                "Execution order: ValuePattern → keyboard.");

            Console.WriteLine(
                "Native Ctrl+A + Unicode input reached exact Edit.");

            Console.WriteLine(
                "Exact text reached Keyboard Only.");

            Console.WriteLine();
            Console.WriteLine(
                "PASS: Assistant keyboard fallback acceptance.");
        }
        finally
        {
            await StopUiFixtureAsync(
                fixture);
        }
    }

    private static async Task
        CheckScreenAssistedResolverLiveAsync()
    {
        string token =
            Guid.NewGuid()
                .ToString("N")[..8];

        string initialTitle =
            $"{UiFixturePrefix} {token}";

        string topInvokedTitle =
            $"{initialTitle} — ASSIST TOP INVOKED";

        string bottomInvokedTitle =
            $"{initialTitle} — ASSIST BOTTOM INVOKED";

        using Process fixture =
            StartUiFixtureProcess(
                token);

        try
        {
            var windows =
                new DesktopWindowTargetService();

            DesktopWindowTarget target =
                await WaitForFixtureWindowAsync(
                    windows,
                    fixture,
                    initialTitle);

            var ui =
                new WindowsDesktopUiAutomationReader();

            DesktopUiSnapshot snapshot =
                await ui.CaptureAsync(
                    target);

            Require(
                snapshot.Success,
                snapshot.Error ??
                "Screen-assisted fixture UIA capture failed.");

            DesktopUiControlResolution initial =
                DesktopUiControlResolver.Resolve(
                    snapshot,
                    "Assist Duplicate",
                    "Button");

            Require(
                initial.Ambiguous,
                "Duplicate fixture buttons were not ambiguous.");

            Require(
                initial.Alternatives.Count ==
                    2,
                $"Expected exactly two duplicate buttons, found {initial.Alternatives.Count}.");

            DesktopUiNodeSnapshot? top =
                initial.Alternatives
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x.AutomationId,
                                "AssistTop",
                                StringComparison.Ordinal));

            DesktopUiNodeSnapshot? bottom =
                initial.Alternatives
                    .FirstOrDefault(
                        x =>
                            string.Equals(
                                x.AutomationId,
                                "AssistBottom",
                                StringComparison.Ordinal));

            Require(
                top is not null &&
                bottom is not null,
                "Duplicate fixture button identities were not captured.");

            Require(
                string.Equals(
                    top!.Name,
                    bottom!.Name,
                    StringComparison.Ordinal),
                "Duplicate fixture names unexpectedly differ.");

            var screen =
                new RecordingScreenEvidenceService(
                    new WindowsDesktopUiScreenEvidenceService());

            var assisted =
                new DesktopUiAssistedResolver(
                    screen);

            var sequence =
                new List<string>();

            var invoke =
                new RecordingUiActionExecutor(
                    new WindowsDesktopUiActionExecutor(),
                    sequence);

            var mouse =
                new RecordingMouseActionExecutor(
                    new WindowsDesktopMouseActionExecutor(),
                    sequence);

            using var handler =
                new FakeHttp(
                    (_, _) =>
                        throw new InvalidOperationException(
                            "Screen-assisted live test attempted Gemini."));

            using var client =
                new HttpClient(
                    handler);

            var chat =
                new ChatCoordinator(
                    new FakeCredentials
                    {
                        Key =
                            "unused-screen-assist-live-key"
                    },
                    new ChatSettings
                    {
                        Provider =
                            ChatProvider.Gemini,

                        UseDesktopActions =
                            true
                    },
                    () => null,
                    client);

            var desktopRouter =
                new LocalDesktopCommandRouter(
                    new DesktopAppCatalogService(
                        () =>
                            Array.Empty<
                                DesktopAppTarget>()),
                    windows);

            var router =
                new AssistantIntentRouter(
                    desktopRouter);

            var action =
                new InvokeDesktopUiControlAction(
                    () => true,
                    windows,
                    ui,
                    invoke,
                    mouse,
                    assisted);

            var assistant =
                new AssistantController(
                    chat,
                    intentRouter:
                        router,
                    actions:
                        new AssistantActionRouter(
                            new IAssistantAction[]
                            {
                                action
                            }));

            string command =
                $"klik tombol Assist Duplicate di window {initialTitle}";

            AssistantReply proposal =
                await assistant.SendAsync(
                    new AssistantRequest(
                        command));

            Require(
                proposal.Backend ==
                    AssistantBackend.Local,
                "Screen-assisted preparation was not local.");

            Require(
                proposal.ActionProposal
                    is not null,
                "Screen-assisted resolution did not produce confirmation.");

            Require(
                screen.Calls == 2,
                $"Expected two native screen evidence probes, got {screen.Calls}.");

            Require(
                screen.Results.TryGetValue(
                    top.Path,
                    out DesktopUiScreenEvidenceResult?
                        topResult),
                "Top duplicate button was not screen-mapped.");

            Require(
                screen.Results.TryGetValue(
                    bottom.Path,
                    out DesktopUiScreenEvidenceResult?
                        bottomResult),
                "Bottom duplicate button was not screen-mapped.");

            Require(
                topResult!.Outcome ==
                    DesktopUiScreenEvidenceOutcome.Captured,
                $"Top button outcome was {topResult.Outcome}, expected Captured.");

            Require(
                bottomResult!.Outcome ==
                    DesktopUiScreenEvidenceOutcome.NotMapped,
                $"Bottom button outcome was {bottomResult.Outcome}, expected NotMapped.");

            Require(
                topResult.Evidence is not null,
                "Captured top button did not return evidence.");

            Require(
                topResult.Evidence!
                    .EncodedBytes
                    .All(
                        value =>
                            value == 0),
                "Captured screen evidence remained in RAM after resolver.");

            Require(
                invoke.Calls == 0 &&
                mouse.Calls == 0 &&
                sequence.Count == 0,
                "Desktop mutation happened before confirmation.");

            Require(
                handler.Calls == 0,
                "Screen-assisted resolution called Gemini.");

            Require(
                assistant.Conversation
                    .GetRecentContext()
                    .Count == 0,
                "Screen-assisted preparation leaked into Gemini context.");

            Require(
                proposal.ActionProposal!
                    .ConfirmationText
                    .Contains(
                        "validasi layar lokal",
                        StringComparison.OrdinalIgnoreCase),
                "Confirmation did not disclose local screen assistance.");

            Require(
                proposal.ActionProposal
                    .ConfirmationText
                    .Contains(
                        "tidak dikirim ke AI",
                        StringComparison.OrdinalIgnoreCase),
                "Confirmation did not disclose screen privacy.");

            AssistantReply confirmed =
                await assistant
                    .ConfirmActionAsync(
                        proposal.ActionProposal.Id);

            Require(
                confirmed.Backend ==
                    AssistantBackend.Local,
                "Screen-assisted execution was not local.");

            Require(
                invoke.Calls == 1,
                "Resolved button was not invoked exactly once.");

            Require(
                mouse.Calls == 0,
                "Mouse fallback ran even though WPF Button exposes InvokePattern.");

            Require(
                sequence.Count == 1 &&
                sequence[0] ==
                    "uia",
                "Unexpected execution sequence.");

            Require(
                screen.Calls == 2,
                "Screen evidence was unexpectedly recaptured during execution.");

            await WaitForFixtureWindowAsync(
                windows,
                fixture,
                topInvokedTitle);

            Require(
                !windows.Capture()
                    .Any(
                        item =>
                            item.ProcessId ==
                                fixture.Id &&
                            string.Equals(
                                item.Title,
                                bottomInvokedTitle,
                                StringComparison.Ordinal)),
                "Covered bottom duplicate button was invoked.");

            Require(
                handler.Calls == 0,
                "Screen-assisted execution called Gemini.");

            Require(
                assistant.Conversation
                    .GetRecentContext()
                    .Count == 0,
                "Screen-assisted result leaked into Gemini context.");

            Console.WriteLine();
            Console.WriteLine(
                "UIA resolver: ambiguous duplicate buttons.");

            Console.WriteLine(
                "AssistTop: Captured.");

            Console.WriteLine(
                "AssistBottom: NotMapped.");

            Console.WriteLine(
                "Screen evidence bytes cleared.");

            Console.WriteLine(
                "Execution: exact AssistTop via UIA Invoke.");

            Console.WriteLine(
                "Gemini calls: 0.");

            Console.WriteLine();
            Console.WriteLine(
                "PASS: native screen-assisted UIA resolver acceptance.");
        }
        finally
        {
            await StopUiFixtureAsync(
                fixture);
        }
    }

    private static async Task<DesktopWindowTarget> WaitForFixtureWindowAsync(
        DesktopWindowTargetService windows,
        Process fixture,
        string expectedTitle)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            fixture.Refresh();
            if (fixture.HasExited)
            {
                throw new InvalidOperationException(
                    $"UIA fixture exited unexpectedly with code {fixture.ExitCode}.");
            }

            DesktopWindowTarget? target = windows.Capture().FirstOrDefault(x =>
                x.ProcessId == fixture.Id &&
                string.Equals(x.Title, expectedTitle, StringComparison.Ordinal));
            if (target is not null)
                return target;
            await Task.Delay(100);
        }

        throw new InvalidOperationException($"UIA fixture window \"{expectedTitle}\" was not found.");
    }

    private static async Task StopUiFixtureAsync(Process fixture)
    {
        fixture.Refresh();
        if (fixture.HasExited)
            return;
        fixture.CloseMainWindow();
        try
        {
            await fixture.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (TimeoutException)
        {
            if (!fixture.HasExited)
            {
                fixture.Kill(entireProcessTree: true);
                await fixture.WaitForExitAsync();
            }
        }
    }
}
