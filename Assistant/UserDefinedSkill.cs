namespace LuKnight.Assistant;

public sealed record UserSkillDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string[] Aliases { get; init; } = [];
    public string[] Steps { get; init; } = [];
}

public sealed class UserDefinedAssistantSkill : IAssistantSkill
{
    private const string ArgumentToken = "{argument}";
    private readonly string[] _steps;
    public string Id { get; }
    public string DisplayName { get; }
    public string Description { get; }
    public IReadOnlyList<string> Aliases { get; }

    public UserDefinedAssistantSkill(UserSkillDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Id = definition.Id.Trim();
        DisplayName = definition.DisplayName.Trim();
        Description = definition.Description.Trim();
        Aliases = Array.AsReadOnly(definition.Aliases.Select(value => value.Trim()).ToArray());
        _steps = definition.Steps.Select(value => value.Trim()).ToArray();
    }

    public SkillExpansionResult Expand(SkillInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        string argument = invocation.Argument.Trim();
        bool needsArgument = _steps.Any(step =>
            step.Contains(ArgumentToken, StringComparison.OrdinalIgnoreCase));
        if (needsArgument && string.IsNullOrWhiteSpace(argument))
            return SkillExpansionResult.Failure($"Skill {DisplayName} membutuhkan parameter.");
        if (!needsArgument && argument.Length > 0)
            return SkillExpansionResult.Failure($"Skill {DisplayName} tidak menerima parameter.");
        if (argument.Length > 500 || argument.Any(char.IsControl))
            return SkillExpansionResult.Failure("Parameter skill tidak valid.");

        AssistantPlanStep[] steps = _steps.Select((template, index) =>
            new AssistantPlanStep(index, template.Replace(
                ArgumentToken, argument, StringComparison.OrdinalIgnoreCase))).ToArray();
        return SkillExpansionResult.Expanded(
            new AssistantPlan(Array.AsReadOnly(steps)), $"Skill {DisplayName} siap.");
    }
}
