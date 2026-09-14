using LuKnight.Models;

namespace LuKnight.Assistant;

public sealed class AssistantActionRouter
{
    private readonly Dictionary<string, IAssistantAction> _actions;
    private readonly
        Func<DesktopPermissionLevel>
        _permissionLevel;

    public AssistantActionRouter(
        IEnumerable<IAssistantAction>? actions = null,
        Func<DesktopPermissionLevel>?
            permissionLevel = null)
    {
        _actions =
            new Dictionary<string, IAssistantAction>(
                StringComparer.OrdinalIgnoreCase);

        _permissionLevel =
            permissionLevel ??
            (() =>
                DesktopPermissionLevel.Interaction);

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

    private ActionPreparationResult
        ApplyPermission(
            ActionPreparationResult prepared)
    {
        if (!prepared.Success ||
            prepared.Action is null)
        {
            return prepared;
        }

        AssistantActionPermissionDecision decision =
            AssistantActionPermissionPolicy
                .Evaluate(
                    prepared.Action,
                    _permissionLevel());

        if (decision.Allowed)
            return prepared;

        // Action tetap disertakan walaupun Success=false
        // agar AssistantController dapat mempertahankan
        // IncludeInContext=false untuk private actions.
        return new ActionPreparationResult(
            false,
            decision.Message,
            prepared.Action);
    }

    public ActionPreparationResult Prepare(
        ActionInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(
            invocation);

        if (!_actions.TryGetValue(
                invocation.Name,
                out IAssistantAction? action))
        {
            return new(
                false,
                "Tindakan itu belum tersedia.");
        }

        return ApplyPermission(
            action.Prepare(
                invocation));
    }

    public async Task<ActionPreparationResult>
        PrepareAsync(
            ActionInvocation invocation,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            invocation);

        cancellationToken.ThrowIfCancellationRequested();

        if (!_actions.TryGetValue(
                invocation.Name,
                out IAssistantAction? action))
        {
            return new(
                false,
                "Tindakan itu belum tersedia.");
        }

        ActionPreparationResult prepared =
            await action.PrepareAsync(
                invocation,
                cancellationToken);

        return ApplyPermission(
            prepared);
    }

    public Task<ActionExecutionResult>
        ExecuteAsync(
            PreparedAssistantAction action,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(
            action);

        cancellationToken.ThrowIfCancellationRequested();

        AssistantActionPermissionDecision permission =
            AssistantActionPermissionPolicy
                .Evaluate(
                    action,
                    _permissionLevel());

        if (!permission.Allowed)
        {
            return Task.FromResult(
                new ActionExecutionResult(
                    false,
                    permission.Message));
        }

        if (!_actions.TryGetValue(
                action.Name,
                out IAssistantAction? executor))
        {
            return Task.FromResult(
                new ActionExecutionResult(
                    false,
                    "Tindakan itu tidak lagi tersedia."));
        }

        return executor.ExecuteAsync(
            action,
            cancellationToken);
    }
}
