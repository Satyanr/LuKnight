using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class AssistantController
{
    private static readonly IReadOnlyDictionary<string, string> EmptyRuntimeVariables =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    private static readonly TimeSpan
        PlanLifetime =
            TimeSpan.FromMinutes(5);

    private static readonly TimeSpan
        StandardConfirmationLifetime =
            TimeSpan.FromMinutes(1);

    private static readonly TimeSpan
        SensitiveFinalLifetime =
            TimeSpan.FromSeconds(30);

    private static readonly TimeSpan
        UiMutationSettleDelay =
            TimeSpan.FromMilliseconds(150);

    private readonly Func<DateTimeOffset>
        _clock;

    private readonly ChatCoordinator _chat;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private PendingAssistantPlan?
        _pendingPlan;
    private sealed record
        PendingAssistantPlan(
            Guid Id,
            AssistantPlan Plan,
            int CurrentStepIndex,
            DateTimeOffset ExpiresAt,
            IReadOnlyDictionary<string, string> RuntimeVariables);

    private PendingAssistantAction? _pendingAction;

    private sealed record PendingAssistantAction(
        Guid Id,
        PreparedAssistantAction Action,
        DateTimeOffset ExpiresAt,
        AssistantConfirmationStage Stage,
        Guid? PlanId = null,
        int? PlanStepIndex = null,
        int? PlanStepCount = null);

    public ConversationManager Conversation { get; } = new();
    public PersonalityEngine Personality { get; }
    public MemoryService Memory { get; }
    public AssistantContextProvider Context { get; }
    public AssistantIntentRouter IntentRouter { get; }
    public AssistantToolRouter Tools { get; }
    public AssistantContextSourceRouter ContextSources { get; }
    public AssistantActionRouter Actions { get; }
    public AssistantSkillRouter Skills { get; }
    public AssistantEmotionEngine Emotions { get; }
    public bool IsBusy => _pendingPlan is not null || _pendingAction is not null || _requestGate.CurrentCount == 0 || _chat.IsBusy;
    public bool HasPendingPlan =>
        _pendingPlan is not null;
    public bool HasPendingAction => _pendingAction is not null;
    public string DisplayName => _chat.DisplayName;

    public AssistantController(
        ChatCoordinator chat,
        PersonalityEngine? personality = null,
        MemoryService? memory = null,
        AssistantContextProvider? context = null,
        AssistantIntentRouter? intentRouter = null,
        AssistantToolRouter? tools = null,
        AssistantEmotionEngine? emotions = null,
        AssistantContextSourceRouter? contextSources = null,
        AssistantActionRouter? actions = null,
        AssistantSkillRouter? skills = null,
        Func<DateTimeOffset>? clock = null)
    {
        _clock =
            clock ??
            (() => DateTimeOffset.UtcNow);
        _chat = chat ?? throw new ArgumentNullException(nameof(chat));
        Personality = personality ?? new PersonalityEngine();
        Memory = memory ?? new MemoryService();
        Context = context ?? new AssistantContextProvider();
        IntentRouter = intentRouter ?? new AssistantIntentRouter();
        Tools = tools ?? new AssistantToolRouter(new IAssistantTool[]
        {
            new RememberMemoryTool(Memory),
            new ForgetMemoryTool(Memory),
            new ListApplicationsTool(() => _chat.Options.UseApplicationContext),
            new InspectDesktopUiTool(
                () => _chat.Options.UseDesktopActions,
                IntentRouter.DesktopWindows,
                new WindowsDesktopUiAutomationReader())
        });
        ContextSources = contextSources ?? new AssistantContextSourceRouter(new IAssistantContextSource[]
        {
            new LocalTextFileContextSource(() => _chat.Options.UseFileContext),
            new ClipboardTextContextSource(() => _chat.Options.UseClipboardContext),
            new SystemStatusContextSource(() => _chat.Options.UseSystemContext),
            new ScreenImageContextSource(() => _chat.Options.UseScreenContext, () => _chat.UsesGemini)
        });
        Actions = actions ?? new AssistantActionRouter(new IAssistantAction[]
        {
            new OpenDesktopApplicationAction(() => _chat.Options.UseDesktopActions, new WindowsDesktopActionExecutor(), IntentRouter.DesktopApps),
            new FocusDesktopApplicationAction(() => _chat.Options.UseDesktopActions, new WindowsDesktopActionExecutor(), IntentRouter.DesktopApps),
            new FocusDesktopWindowAction(() => _chat.Options.UseDesktopActions, IntentRouter.DesktopWindows, new WindowsDesktopWindowActionExecutor()),
            new SetDesktopUiTextAction(
                () =>
                    _chat.Options.UseDesktopActions,
                IntentRouter.DesktopWindows,
                new WindowsDesktopUiAutomationReader(),
                new WindowsDesktopUiTextActionExecutor(),
                new WindowsDesktopKeyboardTextActionExecutor()),
            new InvokeDesktopUiControlAction(
                () => _chat.Options.UseDesktopActions,
                IntentRouter.DesktopWindows,
                new WindowsDesktopUiAutomationReader(),
                new WindowsDesktopUiActionExecutor(),
                new WindowsDesktopMouseActionExecutor(),
                new DesktopUiAssistedResolver(new WindowsDesktopUiScreenEvidenceService())),
            new OpenExplorerFolderAction(() => _chat.Options.UseDesktopActions, new WindowsExplorerActionExecutor()),
            new SearchExplorerAction(() => _chat.Options.UseDesktopActions, new WindowsExplorerActionExecutor())
        }, () => _chat.Options.DesktopPermission);
        Skills = skills ?? new AssistantSkillRouter();
        Emotions = emotions ?? new AssistantEmotionEngine();
    }

    public async Task<AssistantReply> SendAsync(
        AssistantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Text))
            throw new ArgumentException("Pesan tidak boleh kosong.", nameof(request));

        cancellationToken.ThrowIfCancellationRequested();
        bool entered = await _requestGate.WaitAsync(0, cancellationToken);
        if (!entered)
            throw new InvalidOperationException("Tunggu permintaan chat selesai.");

        try
        {
            if (_chat.IsBusy)
                throw new InvalidOperationException("Tunggu permintaan chat selesai.");

            PruneExpiredPendingState();

            if (_pendingAction is not null || _pendingPlan is not null)
                throw new InvalidOperationException("Selesaikan atau batalkan tindakan atau rencana desktop terlebih dahulu.");

            AssistantPlanParseResult plan =
                LocalMultiStepPlanParser.Parse(
                    request.Text);

            if (plan.Recognized)
            {
                if (!plan.Success ||
                    plan.Plan is null)
                {
                    string error =
                        plan.Error ??
                        "Rencana multi-step tidak valid.";

                    Conversation.AddUser(
                        request,
                        includeInContext:
                            false);

                    Conversation.AddAssistant(
                        error,
                        includeInContext:
                            false);

                    return new AssistantReply(
                        error,
                        AssistantBackend.Local,
                        DateTimeOffset.UtcNow,
                        AssistantEmotion.Confused);
                }

                AssistantIntent firstIntent =
                    IntentRouter.Route(
                        plan.Plan.Steps[0].Command);

                // Contoh:
                // "ceritakan tentang kopi lalu teh"
                //
                // Step pertama bukan local command.
                // Jangan planner mengambil alih percakapan biasa.
                if (firstIntent.Kind !=
                    AssistantIntentKind.Conversation)
                {
                    return await StartPlanAsync(
                        request,
                        plan.Plan,
                        firstIntent,
                        cancellationToken);
                }

                // Fall through.
                // Seluruh request diproses sebagai conversation biasa.
            }

            AssistantIntent intent = IntentRouter.Route(request.Text);
            if (intent.Kind == AssistantIntentKind.LocalResponse)
            {
                string message = intent.LocalText ?? "Perintah lokal tidak dapat diproses.";
                Conversation.AddUser(request, intent.IncludeLocalResponseInContext);
                Conversation.AddAssistant(message, intent.IncludeLocalResponseInContext);
                return new AssistantReply(message, AssistantBackend.Local, DateTimeOffset.UtcNow, AssistantEmotion.Neutral);
            }
            if (intent.Kind == AssistantIntentKind.Tool)
            {
                return await ExecuteToolAsync(
                    request,
                    intent.Tool ?? throw new InvalidOperationException("Tool intent tidak memiliki invocation."),
                    cancellationToken);
            }

            if (intent.Kind == AssistantIntentKind.Context)
            {
                ContextInvocation invocation = intent.Context ?? throw new InvalidOperationException("Context intent tidak memiliki invocation.");
                ContextCaptureResult captured = await ContextSources.CaptureAsync(invocation, cancellationToken);
                if (!captured.Success || captured.Reference is null)
                {
                    Conversation.AddUser(request);
                    Conversation.AddAssistant(captured.Message);
                    return new AssistantReply(captured.Message, AssistantBackend.Local, DateTimeOffset.UtcNow, AssistantEmotion.Confused);
                }

                return await SendConversationAsync(
                    request,
                    new[] { captured.Reference },
                    cancellationToken);
            }

            if (intent.Kind == AssistantIntentKind.SkillCatalog)
                return ShowSkillCatalog(request);

            if (intent.Kind == AssistantIntentKind.Skill)
            {
                return await ExecuteSkillAsync(
                    request,
                    intent.Skill ?? throw new InvalidOperationException(
                        "Skill intent tidak memiliki invocation."),
                    cancellationToken);
            }

            if (intent.Kind == AssistantIntentKind.Action)
            {
                return await PrepareActionAsync(
                    request,
                    intent.Action ?? throw new InvalidOperationException("Action intent tidak memiliki invocation."),
                    cancellationToken);
            }

            return await SendConversationAsync(
                request,
                Array.Empty<ChatReferenceBlock>(),
                cancellationToken);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private AssistantReply ShowSkillCatalog(AssistantRequest request)
    {
        IReadOnlyList<AssistantSkillDescriptor> skills = Skills.Catalog;
        string message;
        if (skills.Count == 0)
        {
            message = "Belum ada skill lokal yang tersedia.";
        }
        else
        {
            string entries = string.Join(
                Environment.NewLine,
                skills.Select(skill =>
                    $"- {skill.Id} — {skill.DisplayName}: {skill.Description}"));
            message =
                "Skill lokal yang tersedia:" + Environment.NewLine +
                entries + Environment.NewLine + Environment.NewLine +
                "Gunakan: jalankan skill <nama> atau jalankan skill <nama> dengan <parameter>.";
        }

        Conversation.AddUser(request, includeInContext: false);
        Conversation.AddAssistant(message, includeInContext: false);
        return new AssistantReply(
            message,
            AssistantBackend.Local,
            _clock(),
            AssistantEmotion.Neutral);
    }

    private async Task<AssistantReply> ExecuteSkillAsync(
        AssistantRequest request,
        SkillInvocation invocation,
        CancellationToken cancellationToken)
    {
        SkillExpansionResult expanded = Skills.Expand(invocation);
        if (!expanded.Success || expanded.Plan is null)
        {
            string message = expanded.Message;
            Conversation.AddUser(request, includeInContext: false);
            Conversation.AddAssistant(message, includeInContext: false);
            return new AssistantReply(
                message,
                AssistantBackend.Local,
                _clock(),
                AssistantEmotion.Confused);
        }

        AssistantPlan plan = expanded.Plan;
        AssistantIntent firstIntent = IntentRouter.Route(plan.Steps[0].Command);
        return await StartPlanAsync(
            request,
            plan,
            firstIntent,
            cancellationToken);
    }

    private static AssistantPlan
        FreezePlan(
            AssistantPlan plan)
    {
        ArgumentNullException.ThrowIfNull(
            plan);

        AssistantPlanStep[] steps =
            plan.Steps
                .Select(
                    (step, index) =>
                        new AssistantPlanStep(
                            index,
                            step.Command))
                .ToArray();

        return new AssistantPlan(
            Array.AsReadOnly(
                steps),
            plan.AllowRuntimeVariables);
    }
    private static DateTimeOffset Min(
        DateTimeOffset first,
        DateTimeOffset second) =>
        first <= second
            ? first
            : second;
    private void PruneExpiredPendingState()
    {
        DateTimeOffset now =
            _clock();

        PendingAssistantAction? action =
            _pendingAction;

        if (action is not null &&
            now >= action.ExpiresAt)
        {
            _pendingAction =
                null;

            AbortPlanFor(
                action);
        }

        PendingAssistantPlan? plan =
            _pendingPlan;

        if (plan is not null &&
            now >= plan.ExpiresAt)
        {
            _pendingPlan =
                null;

            if (_pendingAction?.PlanId ==
                plan.Id)
            {
                _pendingAction =
                    null;
            }
        }
    }

    private async Task<AssistantReply>
        StartPlanAsync(
            AssistantRequest request,
            AssistantPlan plan,
            AssistantIntent firstIntent,
            CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(
            plan);

        Conversation.AddUser(
            request,
            includeInContext:
                false);

        DateTimeOffset now =
            _clock();

        _pendingPlan =
            new PendingAssistantPlan(
                Guid.NewGuid(),
                FreezePlan(plan),
                0,
                now.Add(
                    PlanLifetime),
                EmptyRuntimeVariables);

        try
        {
            return await ContinuePlanAsync(
                cancellationToken, firstIntent);
        }
        catch
        {
            _pendingAction =
                null;

            _pendingPlan =
                null;

            throw;
        }
    }
    private async Task<AssistantReply>
        ContinuePlanAsync(
            CancellationToken cancellationToken,
            AssistantIntent? preRoutedIntent = null)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        PendingAssistantPlan plan =
            _pendingPlan ??
            throw new InvalidOperationException(
                "Rencana desktop tidak lagi tersedia.");

        if (plan.CurrentStepIndex >=
            plan.Plan.Count)
        {
            _pendingPlan =
                null;

            const string completed =
                "Semua langkah rencana selesai.";

            Conversation.AddAssistant(
                completed,
                includeInContext:
                    false);

            return new AssistantReply(
                completed,
                AssistantBackend.Local,
                DateTimeOffset.UtcNow,
                AssistantEmotion.Happy);
        }

        if (_clock() >= plan.ExpiresAt)
        {
            _pendingAction = null;
            _pendingPlan = null;
            const string expired = "Rencana desktop sudah kedaluwarsa. Tidak ada langkah lain yang dijalankan.";
            Conversation.AddAssistant(expired, includeInContext: false);
            return new AssistantReply(expired, AssistantBackend.Local, _clock(), AssistantEmotion.Confused);
        }

        AssistantPlanStep step =
            plan.Plan.Steps[
                plan.CurrentStepIndex];

        string command = step.Command;
        if (plan.Plan.AllowRuntimeVariables && preRoutedIntent is null)
        {
            WorkflowRuntimeResolution resolution =
                WorkflowRuntimeVariableResolver.Resolve(command, plan.RuntimeVariables);
            if (!resolution.Success || resolution.Command is null)
            {
                _pendingPlan = null;
                string reason = resolution.Error ??
                    "Variable workflow tidak dapat diselesaikan.";
                string message =
                    $"Rencana dihentikan pada langkah {step.Index + 1}: {reason}";
                Conversation.AddAssistant(message, includeInContext: false);
                return new AssistantReply(
                    message, AssistantBackend.Local, _clock(), AssistantEmotion.Confused);
            }
            command = resolution.Command;
        }

        // IMPORTANT:
        // route dilakukan baru sekarang.
        AssistantIntent intent =
            preRoutedIntent ?? IntentRouter.Route(
                command);
        if (intent.Kind !=
            AssistantIntentKind.Action ||
            intent.Action is null)
        {
            _pendingPlan =
                null;

            string reason =
                intent.Kind ==
                    AssistantIntentKind.LocalResponse
                    ? intent.LocalText ??
                        "Langkah ditolak oleh router lokal."
                    : "Jenis langkah ini belum didukung di rencana multi-step lokal.";

            string message =
                $"Rencana dihentikan pada langkah {step.Index + 1}: {reason}";

            Conversation.AddAssistant(
                message,
                includeInContext:
                    false);

            return new AssistantReply(
                message,
                AssistantBackend.Local,
                DateTimeOffset.UtcNow,
                AssistantEmotion.Confused);
        }
        ActionPreparationResult prepared =
            await Actions.PrepareAsync(
                intent.Action,
                cancellationToken);

        if (!prepared.Success ||
            prepared.Action is null)
        {
            _pendingPlan =
                null;

            string message =
                $"Rencana dihentikan pada langkah {step.Index + 1}: {prepared.Message}";

            Conversation.AddAssistant(
                message,
                includeInContext:
                    false);

            return new AssistantReply(
                message,
                AssistantBackend.Local,
                DateTimeOffset.UtcNow,
                AssistantEmotion.Confused);
        }
        PreparedAssistantAction action =
            prepared.Action with
            {
                IncludeInContext =
                    false
            };
        AssistantConfirmationStage stage =
            action.Risk ==
                AssistantActionRisk.Sensitive
                ? AssistantConfirmationStage
                    .SensitiveReview
                : AssistantConfirmationStage
                    .Standard;
        Guid proposalId =
            Guid.NewGuid();

        DateTimeOffset now =
            _clock();

        if (now >=
            plan.ExpiresAt)
        {
            _pendingAction =
                null;

            _pendingPlan =
                null;

            const string expired =
                "Rencana desktop sudah kedaluwarsa. Tidak ada langkah lain yang dijalankan.";

            Conversation.AddAssistant(
                expired,
                includeInContext:
                    false);

            return new AssistantReply(
                expired,
                AssistantBackend.Local,
                DateTimeOffset.UtcNow,
                AssistantEmotion.Confused);
        }

        DateTimeOffset expiresAt =
            Min(
                now.Add(
                    StandardConfirmationLifetime),
                plan.ExpiresAt);

        _pendingAction =
            new PendingAssistantAction(
                proposalId,
                action,
                expiresAt,
                stage,
                PlanId:
                    plan.Id,
                PlanStepIndex:
                    step.Index,
                PlanStepCount: plan.Plan.Count);
        string proposalMessage =
            $"Rencana langkah {step.Index + 1}/{plan.Plan.Count} memerlukan konfirmasi.";

        Conversation.AddAssistant(
            proposalMessage,
            includeInContext:
                false);

        return new AssistantReply(
            proposalMessage,
            AssistantBackend.Local,
            DateTimeOffset.UtcNow,
            AssistantEmotion.Determined,
            BuildActionProposal(
                _pendingAction));
    }
    private void AbortPlanFor(
        PendingAssistantAction pending)
    {
        if (pending.PlanId is not
            Guid planId)
        {
            return;
        }

        if (_pendingPlan?.Id ==
            planId)
        {
            _pendingPlan =
                null;
        }
    }
    private async Task<AssistantReply>
        CompletePlanStepAsync(
            PendingAssistantAction pending,
            Guid planId,
            ActionExecutionResult result,
            CancellationToken cancellationToken)
    {
        Conversation.AddAssistant(
            result.Message,
            includeInContext:
                false);

        PendingAssistantPlan? plan =
            _pendingPlan;

        if (plan is null ||
            plan.Id != planId ||
            pending.PlanStepIndex is not
                int stepIndex ||
            plan.CurrentStepIndex !=
                stepIndex)
        {
            _pendingPlan =
                null;

            const string stateChanged =
                "Langkah desktop selesai, tetapi state rencana berubah. Sisa rencana dihentikan.";

            Conversation.AddAssistant(
                stateChanged,
                includeInContext:
                    false);

            return new AssistantReply(
                stateChanged,
                AssistantBackend.Local,
                DateTimeOffset.UtcNow,
                AssistantEmotion.Confused);
        }

        if (!result.Success)
        {
            _pendingPlan =
                null;

            string failed =
                $"Rencana dihentikan pada langkah {stepIndex + 1}: {result.Message}";

            Conversation.AddAssistant(
                failed,
                includeInContext:
                    false);

            return new AssistantReply(
                failed,
                AssistantBackend.Local,
                DateTimeOffset.UtcNow,
                AssistantEmotion.Confused);
        }

        if (_clock() >=
            plan.ExpiresAt)
        {
            _pendingPlan =
                null;

            string expired =
                $"Langkah {stepIndex + 1}/{plan.Plan.Count} selesai, " +
                "tetapi batas waktu rencana sudah habis. Sisa langkah dihentikan.";

            Conversation.AddAssistant(
                expired,
                includeInContext:
                    false);

            return new AssistantReply(
                expired,
                AssistantBackend.Local,
                DateTimeOffset.UtcNow,
                AssistantEmotion.Neutral);
        }

        int nextIndex =
            stepIndex + 1;

        if (nextIndex >=
            plan.Plan.Count)
        {
            _pendingPlan =
                null;

            string completed =
                $"Langkah {stepIndex + 1}/{plan.Plan.Count} selesai. Semua langkah rencana selesai.";

            Conversation.AddAssistant(
                completed,
                includeInContext:
                    false);

            return new AssistantReply(
                completed,
                AssistantBackend.Local,
                DateTimeOffset.UtcNow,
                AssistantEmotion.Happy);
        }

        if (NeedsUiSettle(pending.Action))
        {
            await Task.Delay(UiMutationSettleDelay, cancellationToken);
            if (_clock() >= plan.ExpiresAt)
            {
                _pendingPlan = null;
                string expired =
                    $"Langkah {stepIndex + 1}/{plan.Plan.Count} selesai, " +
                    "tetapi batas waktu rencana habis saat menunggu UI stabil. " +
                    "Sisa langkah dihentikan.";
                Conversation.AddAssistant(expired, includeInContext: false);
                return new AssistantReply(
                    expired, AssistantBackend.Local, _clock(), AssistantEmotion.Neutral);
            }
        }

        IReadOnlyDictionary<string, string> runtimeVariables =
            plan.Plan.AllowRuntimeVariables
                ? CaptureRuntimeVariables(pending.Action)
                : EmptyRuntimeVariables;

        _pendingPlan =
            plan with
            {
                CurrentStepIndex =
                    nextIndex,
                RuntimeVariables = runtimeVariables
            };

        return await ContinuePlanAsync(
            cancellationToken);
    }

    private static bool NeedsUiSettle(PreparedAssistantAction action) =>
        action.Name is
            BuiltInActionNames.DesktopInvokeUiControl or
            BuiltInActionNames.DesktopSetUiText;

    private IReadOnlyDictionary<string, string> CaptureRuntimeVariables(
        PreparedAssistantAction action)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!action.Arguments.TryGetValue("windowId", out string? windowId) ||
            !IntentRouter.DesktopWindows.TryResolveById(windowId, out DesktopWindowTarget window))
            return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(result);
        AddRuntimeValue(result, "last.window", window.Title, 200);
        AddRuntimeValue(result, "last.process", window.ProcessName, 100);
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(result);
    }

    private static void AddRuntimeValue(
        IDictionary<string, string> destination,
        string name,
        string? rawValue,
        int maximumLength)
    {
        string value = rawValue?.Trim() ?? string.Empty;
        if (value.Length is < 1 || value.Length > maximumLength || value.Any(char.IsControl))
            return;
        destination[name] = value;
    }

    private async Task<AssistantReply> PrepareActionAsync(
        AssistantRequest request,
        ActionInvocation invocation,
        CancellationToken cancellationToken)
    {
        ActionPreparationResult prepared =
            await Actions.PrepareAsync(invocation, cancellationToken);
        bool includeInContext = prepared.Action?.IncludeInContext ?? invocation.IncludeInContext;
        Conversation.AddUser(request, includeInContext);
        if (!prepared.Success || prepared.Action is null)
        {
            Conversation.AddAssistant(prepared.Message, includeInContext);
            return new AssistantReply(prepared.Message, AssistantBackend.Local, DateTimeOffset.UtcNow, AssistantEmotion.Confused);
        }

        Guid id = Guid.NewGuid();
        DateTimeOffset expiresAt = _clock().Add(StandardConfirmationLifetime);
        AssistantConfirmationStage stage =
            prepared.Action.Risk ==
                AssistantActionRisk.Sensitive
                ? AssistantConfirmationStage
                    .SensitiveReview
                : AssistantConfirmationStage
                    .Standard;

        _pendingAction =
            new PendingAssistantAction(
                id,
                prepared.Action,
                expiresAt,
                stage);

        string message = $"Tindakan desktop memerlukan konfirmasi: {prepared.Action.Title}.";
        Conversation.AddAssistant(message, prepared.Action.IncludeInContext);
        return new AssistantReply(message, AssistantBackend.Local, DateTimeOffset.UtcNow, AssistantEmotion.Determined,
            BuildActionProposal(_pendingAction));
    }

    private static AssistantActionProposal
        BuildActionProposal(
            PendingAssistantAction pending)
    {
        PreparedAssistantAction action =
            pending.Action;

        int? planStepNumber =
            pending.PlanStepIndex is int stepIndex
                ? stepIndex + 1
                : null;

        int? planStepCount =
            pending.PlanStepCount;

        bool isPlanStep =
            planStepNumber is not null &&
            planStepCount is not null;
        string planPrefix =
            isPlanStep
                ? $"Rencana {planStepNumber}/{planStepCount} — "
                : string.Empty;
        string title =
            pending.Stage switch
            {
                AssistantConfirmationStage.SensitiveReview =>
                    $"{planPrefix}Tinjau tindakan sensitif — {action.Title}",

                AssistantConfirmationStage.SensitiveFinal =>
                    $"{planPrefix}Konfirmasi akhir — {action.Title}",

                _ =>
                    $"{planPrefix}{action.Title}"
            };
        string planNotice =
            isPlanStep
                ? $"\n\nIni hanya mengizinkan langkah {planStepNumber} dari {planStepCount}. " +
                  "Langkah berikutnya tetap memerlukan konfirmasi tersendiri."
                : string.Empty;

        string confirmation =
            pending.Stage switch
            {
                AssistantConfirmationStage
                    .SensitiveReview =>
                    "PERINGATAN: Lu-Knight mengklasifikasikan tindakan ini sebagai sensitif.\n\n" +
                    action.ConfirmationText +
                    "\n\nMemilih Yes pada tahap ini BELUM menjalankan tindakan. " +
                    "Lu-Knight akan meminta satu konfirmasi akhir.",

                AssistantConfirmationStage
                    .SensitiveFinal =>
                    "KONFIRMASI AKHIR.\n\n" +
                    action.ConfirmationText +
                    "\n\nJika memilih Yes sekarang, tindakan akan dijalankan.",

                _ =>
                    action.ConfirmationText
            };

        confirmation +=
            planNotice;
        return new AssistantActionProposal(
            pending.Id,
            title,
            confirmation,
            pending.ExpiresAt,
            action.Risk,
            pending.Stage,
            planStepNumber,
            planStepCount);

    }

    public async Task<AssistantReply> ConfirmActionAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        bool entered = await _requestGate.WaitAsync(0);
        if (!entered)
            throw new InvalidOperationException("Tunggu permintaan sebelumnya selesai.");

        PendingAssistantAction? pending = _pendingAction;
        try
        {
            if (pending is null || pending.Id != proposalId)
                throw new InvalidOperationException("Konfirmasi tindakan tidak lagi valid.");

            if (cancellationToken.IsCancellationRequested)
            {
                _pendingAction = null;
                AbortPlanFor(pending);
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (_clock() >= pending.ExpiresAt)
            {
                _pendingAction = null;
                AbortPlanFor(pending);
                const string expired = "Konfirmasi tindakan sudah kedaluwarsa.";
                Conversation.AddAssistant(expired, pending.Action.IncludeInContext);
                return new AssistantReply(expired, AssistantBackend.Local, DateTimeOffset.UtcNow, AssistantEmotion.Confused);
            }

            if (pending.PlanId is
                    Guid expiryPlanId &&
                _pendingPlan is
                    { } expiryPlan &&
                expiryPlan.Id ==
                    expiryPlanId &&
                _clock() >=
                    expiryPlan.ExpiresAt)
            {
                _pendingAction =
                    null;

                _pendingPlan =
                    null;

                const string expired =
                    "Rencana desktop sudah kedaluwarsa. Tindakan tidak dijalankan.";

                Conversation.AddAssistant(
                    expired,
                    includeInContext:
                        false);

                return new AssistantReply(
                    expired,
                    AssistantBackend.Local,
                    DateTimeOffset.UtcNow,
                    AssistantEmotion.Confused);
            }

            if (pending.Stage ==
                    AssistantConfirmationStage
                        .SensitiveReview)
            {
                AssistantActionPermissionDecision permission =
                    Actions.CheckPermission(
                        pending.Action);

                if (!permission.Allowed)
                {
                    _pendingAction =
                        null;

                    AbortPlanFor(pending);
                    Conversation.AddAssistant(
                        permission.Message,
                        pending.Action.IncludeInContext);

                    return new AssistantReply(
                        permission.Message,
                        AssistantBackend.Local,
                        DateTimeOffset.UtcNow,
                        AssistantEmotion.Confused);
                }

                Guid finalId =
                    Guid.NewGuid();

                DateTimeOffset now =
                    _clock();

                DateTimeOffset finalExpiry =
                    now.Add(
                        SensitiveFinalLifetime);

                if (pending.PlanId is
                        Guid finalPlanId &&
                    _pendingPlan is
                        { } plan &&
                    plan.Id ==
                        finalPlanId)
                {
                    if (now >=
                        plan.ExpiresAt)
                    {
                        _pendingAction =
                            null;

                        _pendingPlan =
                            null;

                        const string expired =
                            "Rencana desktop sudah kedaluwarsa sebelum konfirmasi akhir.";

                        Conversation.AddAssistant(
                            expired,
                            includeInContext:
                                false);

                        return new AssistantReply(
                            expired,
                            AssistantBackend.Local,
                            DateTimeOffset.UtcNow,
                            AssistantEmotion.Confused);
                    }

                    finalExpiry =
                        Min(
                            finalExpiry,
                            plan.ExpiresAt);
                }

                PendingAssistantAction finalPending =
                    pending with
                    {
                        Id =
                            finalId,

                        ExpiresAt =
                            finalExpiry,

                        Stage =
                            AssistantConfirmationStage
                                .SensitiveFinal
                    };

                _pendingAction =
                    finalPending;

                const string message =
                    "Tindakan sensitif belum dijalankan. Konfirmasi akhir diperlukan.";

                Conversation.AddAssistant(
                    message,
                    pending.Action
                        .IncludeInContext);

                return new AssistantReply(
                    message,
                    AssistantBackend.Local,
                    DateTimeOffset.UtcNow,
                    AssistantEmotion.Determined,
                    BuildActionProposal(
                        finalPending));
            }

            _pendingAction =
                null;

            AssistantActionConfirmation authorization =
                pending.Stage ==
                    AssistantConfirmationStage
                        .SensitiveFinal
                    ? AssistantActionConfirmation
                        .Strong
                    : AssistantActionConfirmation
                        .Standard;

            PreparedAssistantAction authorized =
                pending.Action with
                {
                    Confirmation =
                        authorization
                };

            ActionExecutionResult result =
                await Actions.ExecuteAsync(
                    authorized,
                    cancellationToken);

            if (pending.PlanId is
                    Guid planId)
            {
                return await CompletePlanStepAsync(
                    pending,
                    planId,
                    result,
                    cancellationToken);
            }

            Conversation.AddAssistant(result.Message, pending.Action.IncludeInContext);
            return new AssistantReply(result.Message, AssistantBackend.Local, DateTimeOffset.UtcNow,
                result.Success ? AssistantEmotion.Happy : AssistantEmotion.Confused);
        }
        catch
        {
            if (pending is not null && pending.Id == proposalId)
            {
                _pendingAction = null;
                AbortPlanFor(pending);
            }
            throw;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    public AssistantReply CancelAction(Guid proposalId)
    {
        if (!_requestGate.Wait(0))
            throw new InvalidOperationException("Tunggu permintaan sebelumnya selesai.");

        try
        {
            PendingAssistantAction? pending = _pendingAction;
            if (pending is null || pending.Id != proposalId)
                throw new InvalidOperationException("Konfirmasi tindakan tidak lagi valid.");

            _pendingAction = null;
            AbortPlanFor(pending);
            string message =
                pending.PlanId is not null
                    ? "Rencana desktop dibatalkan. Sisa langkah tidak dijalankan."
                    : "Tindakan desktop dibatalkan.";

            Conversation.AddAssistant(message, pending.Action.IncludeInContext);
            return new AssistantReply(message, AssistantBackend.Local, DateTimeOffset.UtcNow, AssistantEmotion.Neutral);
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task<AssistantReply> ExecuteToolAsync(
        AssistantRequest request,
        ToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        Conversation.AddUser(request, invocation.IncludeInContext);
        try
        {
            ToolExecutionResult result = await Tools.ExecuteAsync(invocation, cancellationToken);
            Conversation.AddAssistant(result.Message, invocation.IncludeInContext);
            AssistantEmotion emotion = Emotions.EvaluateTool(invocation, result);
            return new AssistantReply(result.Message, AssistantBackend.Local, DateTimeOffset.UtcNow, emotion);
        }
        catch
        {
            Conversation.RollbackPendingUser();
            throw;
        }
    }

    private async Task<AssistantReply> SendConversationAsync(
        AssistantRequest request,
        IReadOnlyList<ChatReferenceBlock> references,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ChatContextTurn> context = BuildShortTermContext();
        Conversation.AddUser(request);

        try
        {
            string personalityInstruction = Personality.BuildSystemInstruction();
            string runtimeInstruction = Context.BuildSystemInstruction();
            if (!string.IsNullOrWhiteSpace(runtimeInstruction))
                personalityInstruction += "\n\n" + runtimeInstruction;

            IReadOnlyList<string> longTermMemory = BuildLongTermMemoryContext(request.Text);

            string reply = await _chat.SendMessageAsync(
                request.Text,
                personalityInstruction,
                context,
                longTermMemory,
                references,
                cancellationToken);

            Conversation.AddAssistant(reply);

            AssistantBackend backend = _chat.LastReplyWasGemini ? AssistantBackend.Gemini : AssistantBackend.Local;
            AssistantEmotion emotion = Emotions.EvaluateConversation(
                request.Text,
                reply,
                backend,
                _chat.Options.Style);
            return new AssistantReply(reply, backend, DateTimeOffset.UtcNow, emotion);
        }
        catch
        {
            Conversation.RollbackPendingUser();
            throw;
        }
    }

    private IReadOnlyList<string> BuildLongTermMemoryContext(string query)
    {
        if (!_chat.Options.UseLongTermMemory)
            return Array.Empty<string>();

        return Memory.Search(query).Select(memory => memory.Text).ToArray();
    }

    private IReadOnlyList<ChatContextTurn> BuildShortTermContext()
    {
        if (!_chat.Options.RememberConversation)
            return Array.Empty<ChatContextTurn>();

        return Conversation.GetRecentContext()
            .Select(turn => new ChatContextTurn(
                turn.Role == ConversationRole.User
                    ? ChatContextRole.User
                    : ChatContextRole.Assistant,
                turn.Text))
            .ToArray();
    }

    public void ClearConversation()
    {
        if (!_requestGate.Wait(0))
        {
            throw new InvalidOperationException("Tunggu permintaan chat selesai.");
        }

        try
        {
            if (_pendingAction is not null || _pendingPlan is not null)
            {
                throw new InvalidOperationException("Selesaikan atau batalkan tindakan atau rencana desktop terlebih dahulu.");
            }

            _chat.ClearConversation();
            Conversation.Clear();
        }
        finally
        {
            _requestGate.Release();
        }
    }
}
