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
        AssistantEmotionEngine? emotions = null)
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
