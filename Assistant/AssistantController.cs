using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class AssistantController
{
    private readonly ChatCoordinator _chat;
    private readonly SemaphoreSlim _requestGate = new(1, 1);

    public ConversationManager Conversation { get; } = new();
    public PersonalityEngine Personality { get; }
    public MemoryService Memory { get; }
    public AssistantContextProvider Context { get; }
    public AssistantIntentRouter IntentRouter { get; }
    public AssistantToolRouter Tools { get; }
    public AssistantContextSourceRouter ContextSources { get; }
    public AssistantEmotionEngine Emotions { get; }
    public bool IsBusy => _requestGate.CurrentCount == 0 || _chat.IsBusy;
    public string DisplayName => _chat.DisplayName;

    public AssistantController(
        ChatCoordinator chat,
        PersonalityEngine? personality = null,
        MemoryService? memory = null,
        AssistantContextProvider? context = null,
        AssistantIntentRouter? intentRouter = null,
        AssistantToolRouter? tools = null,
        AssistantEmotionEngine? emotions = null,
        AssistantContextSourceRouter? contextSources = null)
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
            new ListApplicationsTool(() => _chat.Options.UseApplicationContext)
        });
        ContextSources = contextSources ?? new AssistantContextSourceRouter(new IAssistantContextSource[]
        {
            new LocalTextFileContextSource(() => _chat.Options.UseFileContext),
            new ClipboardTextContextSource(() => _chat.Options.UseClipboardContext)
        });
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

            AssistantIntent intent = IntentRouter.Route(request.Text);
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

    private async Task<AssistantReply> ExecuteToolAsync(
        AssistantRequest request,
        ToolInvocation invocation,
        CancellationToken cancellationToken)
    {
        Conversation.AddUser(request);
        try
        {
            ToolExecutionResult result = await Tools.ExecuteAsync(invocation, cancellationToken);
            Conversation.AddAssistant(result.Message);
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
            throw new InvalidOperationException("Tunggu permintaan chat selesai.");

        try
        {
            _chat.ClearConversation();
            Conversation.Clear();
        }
        finally
        {
            _requestGate.Release();
        }
    }
}
