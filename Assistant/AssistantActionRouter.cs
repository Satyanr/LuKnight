namespace LuKnight.Assistant;

public sealed class AssistantActionRouter
{
    private readonly Dictionary<string, IAssistantAction> _actions;

    public AssistantActionRouter(IEnumerable<IAssistantAction>? actions = null)
    {
        _actions = new Dictionary<string, IAssistantAction>(StringComparer.OrdinalIgnoreCase);

        if (actions is null)
            return;

        foreach (IAssistantAction action in actions)
            Register(action);
    }

    public IReadOnlyCollection<string> RegisteredActions => _actions.Keys.ToArray();

    public void Register(IAssistantAction action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!_actions.TryAdd(action.Name, action))
            throw new InvalidOperationException($"Action '{action.Name}' sudah terdaftar.");
    }

    public ActionPreparationResult Prepare(ActionInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);

        if (!_actions.TryGetValue(invocation.Name, out IAssistantAction? action))
            return new ActionPreparationResult(false, "Tindakan itu belum tersedia.");

        return action.Prepare(invocation);
    }

    public Task<ActionExecutionResult> ExecuteAsync(PreparedAssistantAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_actions.TryGetValue(action.Name, out IAssistantAction? executor))
            return Task.FromResult(new ActionExecutionResult(false, "Tindakan itu tidak lagi tersedia."));

        return executor.ExecuteAsync(action, cancellationToken);
    }
}
