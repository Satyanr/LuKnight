namespace LuKnight.Assistant;

public sealed record AssistantPlanStep(
    int Index,
    string Command);

public sealed record AssistantPlan(
    IReadOnlyList<AssistantPlanStep> Steps)
{
    public int Count =>
        Steps.Count;
}

public sealed record AssistantPlanParseResult(
    bool Recognized,
    AssistantPlan? Plan = null,
    string? Error = null)
{
    public bool Success =>
        Recognized &&
        Plan is not null &&
        string.IsNullOrWhiteSpace(
            Error);

    public static AssistantPlanParseResult
        NotRecognized() =>
        new(
            false);

    public static AssistantPlanParseResult
        Failure(
            string error) =>
        new(
            true,
            null,
            error);

    public static AssistantPlanParseResult
        Parsed(
            AssistantPlan plan) =>
        new(
            true,
            plan);
}
