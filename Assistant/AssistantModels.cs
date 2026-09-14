namespace LuKnight.Assistant;

public enum AssistantInputSource
{
    Chat,
    Tray,
    Voice,
    System
}

public enum AssistantBackend
{
    Local,
    Gemini
}

public enum ConversationRole
{
    User,
    Assistant
}

public sealed record AssistantRequest(
    string Text,
    AssistantInputSource Source = AssistantInputSource.Chat);

public enum AssistantConfirmationStage
{
    Standard,
    SensitiveReview,
    SensitiveFinal
}

public sealed record AssistantActionProposal(
    Guid Id,
    string Title,
    string ConfirmationText,
    DateTimeOffset ExpiresAt,
    AssistantActionRisk Risk =
        AssistantActionRisk.Interaction,
    AssistantConfirmationStage ConfirmationStage =
        AssistantConfirmationStage.Standard,
    int? PlanStepNumber = null,
    int? PlanStepCount = null)
{
    public bool IsPlanStep =>
        PlanStepNumber is not null &&
        PlanStepCount is not null;
}

public sealed record AssistantReply(
    string Text,
    AssistantBackend Backend,
    DateTimeOffset CreatedAt,
    AssistantEmotion Emotion = AssistantEmotion.Neutral,
    AssistantActionProposal? ActionProposal = null);

public sealed record ConversationTurn(
    ConversationRole Role,
    string Text,
    DateTimeOffset CreatedAt,
    AssistantInputSource Source = AssistantInputSource.Chat,
    bool IncludeInContext = true);
