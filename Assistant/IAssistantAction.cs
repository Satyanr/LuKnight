namespace LuKnight.Assistant;

public enum AssistantActionRisk
{
    Navigation,
    Interaction,
    Sensitive,
    Prohibited
}

public enum AssistantActionConfirmation
{
    None,
    Standard,
    Strong
}

public sealed record PreparedAssistantAction(
    string Name,
    IReadOnlyDictionary<string, string> Arguments,
    string Title,
    string ConfirmationText,
    bool IncludeInContext = true,
    AssistantActionRisk Risk =
        AssistantActionRisk.Interaction,
    AssistantActionConfirmation Confirmation =
        AssistantActionConfirmation.None);

public sealed record ActionPreparationResult(
    bool Success,
    string Message,
    PreparedAssistantAction? Action = null);

public sealed record ActionExecutionResult(
    bool Success,
    string Message);

public interface IAssistantAction
{
    string Name { get; }

    ActionPreparationResult Prepare(ActionInvocation invocation);

    Task<ActionPreparationResult> PrepareAsync(
        ActionInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Prepare(invocation));
    }

    Task<ActionExecutionResult> ExecuteAsync(
        PreparedAssistantAction action,
        CancellationToken cancellationToken = default);
}
