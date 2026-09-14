using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class AssistantController
{
    private readonly ChatCoordinator _chat;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private PendingAssistantAction? _pendingAction;

    private sealed record PendingAssistantAction(
        Guid Id,
        PreparedAssistantAction Action,
        DateTimeOffset ExpiresAt,
        AssistantConfirmationStage Stage);

    public ConversationManager Conversation { get; } = new();
    public PersonalityEngine Personality { get; }
    public MemoryService Memory { get; }
    public AssistantContextProvider Context { get; }
    public AssistantIntentRouter IntentRouter { get; }
    public AssistantToolRouter Tools { get; }
    public AssistantContextSourceRouter ContextSources { get; }
    public AssistantActionRouter Actions { get; }
    public AssistantEmotionEngine Emotions { get; }
    public bool IsBusy => _pendingAction is not null || _requestGate.CurrentCount == 0 || _chat.IsBusy;
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
        AssistantActionRouter? actions = null)
    {
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

            if (_pendingAction is not null)
                throw new InvalidOperationException("Selesaikan konfirmasi tindakan desktop terlebih dahulu.");

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
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.AddMinutes(1);
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

        string title =
            pending.Stage switch
            {
                AssistantConfirmationStage
                    .SensitiveReview =>
                    $"Tinjau tindakan sensitif — {action.Title}",

                AssistantConfirmationStage
                    .SensitiveFinal =>
                    $"Konfirmasi akhir — {action.Title}",

                _ =>
                    action.Title
            };

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

        return new AssistantActionProposal(
            pending.Id,
            title,
            confirmation,
            pending.ExpiresAt,
            action.Risk,
            pending.Stage);
    }

    public async Task<AssistantReply> ConfirmActionAsync(Guid proposalId, CancellationToken cancellationToken = default)
    {
        bool entered = await _requestGate.WaitAsync(0);
        if (!entered)
            throw new InvalidOperationException("Tunggu permintaan sebelumnya selesai.");

        try
        {
            PendingAssistantAction? pending = _pendingAction;
            if (pending is null || pending.Id != proposalId)
                throw new InvalidOperationException("Konfirmasi tindakan tidak lagi valid.");

            if (cancellationToken.IsCancellationRequested)
            {
                _pendingAction = null;
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (DateTimeOffset.UtcNow > pending.ExpiresAt)
            {
                _pendingAction = null;
                const string expired = "Konfirmasi tindakan sudah kedaluwarsa.";
                Conversation.AddAssistant(expired, pending.Action.IncludeInContext);
                return new AssistantReply(expired, AssistantBackend.Local, DateTimeOffset.UtcNow, AssistantEmotion.Confused);
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

                DateTimeOffset finalExpiry =
                    DateTimeOffset.UtcNow
                        .AddSeconds(30);

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

            Conversation.AddAssistant(result.Message, pending.Action.IncludeInContext);
            return new AssistantReply(result.Message, AssistantBackend.Local, DateTimeOffset.UtcNow,
                result.Success ? AssistantEmotion.Happy : AssistantEmotion.Confused);
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
            const string message = "Tindakan desktop dibatalkan.";
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
            if (_pendingAction is not null)
            {
                throw new InvalidOperationException("Selesaikan atau batalkan tindakan desktop terlebih dahulu.");
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
