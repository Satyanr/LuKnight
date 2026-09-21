namespace LuKnight.Assistant;

public sealed record SkillInvocation(
    string Name,
    string Argument = "",
    bool IncludeInContext = false);

public sealed record SkillExpansionResult(
    bool Success,
    string Message,
    AssistantPlan? Plan = null)
{
    public static SkillExpansionResult Failure(string message) =>
        new(false, message);

    public static SkillExpansionResult Expanded(
        AssistantPlan plan,
        string message = "Skill siap dijalankan.") =>
        new(true, message, plan);
}

public interface IAssistantSkill
{
    string Id { get; }
    string DisplayName { get; }
    string Description { get; }
    IReadOnlyList<string> Aliases { get; }
    SkillExpansionResult Expand(SkillInvocation invocation);
}

public sealed record AssistantSkillDescriptor(
    string Id,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Aliases);
