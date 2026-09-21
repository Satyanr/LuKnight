namespace LuKnight.Assistant;

public sealed record UserSkillDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public bool Enabled { get; init; } = true;
    public string Id { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string[] Aliases { get; init; } = [];
    public UserSkillParameterDefinition[] Parameters { get; init; } = [];
    public string[] Steps { get; init; } = [];
}

public sealed record UserSkillParameterDefinition
{
    public string Name { get; init; } = string.Empty;
    public bool Required { get; init; } = true;
    public int MaxLength { get; init; } = 200;
}

public sealed class UserDefinedAssistantSkill : IAssistantSkill
{
    private const string ArgumentToken = "{argument}";
    private readonly string[] _steps;
    private readonly UserSkillParameterDefinition[] _parameters;
    private readonly int _schemaVersion;
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
        _schemaVersion = definition.SchemaVersion;
        _parameters = definition.Parameters.Select(parameter => parameter with
        {
            Name = parameter.Name.Trim()
        }).ToArray();
    }

    public SkillExpansionResult Expand(SkillInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        return _schemaVersion switch
        {
            1 => ExpandLegacy(invocation),
            2 => ExpandNamed(invocation),
            _ => SkillExpansionResult.Failure("Schema skill tidak didukung.")
        };
    }

    private SkillExpansionResult ExpandLegacy(SkillInvocation invocation)
    {
        if (invocation.Parameters is { Count: > 0 })
            return SkillExpansionResult.Failure(
                $"Skill {DisplayName} hanya menerima parameter tunggal.");
        string argument = invocation.Argument.Trim();
        bool needsArgument = _steps.Any(step =>
            step.Contains(ArgumentToken, StringComparison.OrdinalIgnoreCase));
        if (needsArgument && string.IsNullOrWhiteSpace(argument))
            return SkillExpansionResult.Failure($"Skill {DisplayName} membutuhkan parameter.");
        if (!needsArgument && argument.Length > 0)
            return SkillExpansionResult.Failure($"Skill {DisplayName} tidak menerima parameter.");
        if (argument.Length > AssistantSkillPolicy.MaxParameterValueLength ||
            argument.Any(char.IsControl))
            return SkillExpansionResult.Failure("Parameter skill tidak valid.");

        return ExpandSteps(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["argument"] = argument
        });
    }

    private SkillExpansionResult ExpandNamed(SkillInvocation invocation)
    {
        if (!string.IsNullOrWhiteSpace(invocation.Argument))
            return SkillExpansionResult.Failure(
                $"Skill {DisplayName} membutuhkan named parameters.");
        IReadOnlyDictionary<string, string> supplied = invocation.Parameters ??
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var declared = _parameters.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        foreach (string key in supplied.Keys)
            if (!declared.ContainsKey(key))
                return SkillExpansionResult.Failure($"Parameter '{key}' tidak dikenal.");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        foreach (UserSkillParameterDefinition parameter in _parameters)
        {
            supplied.TryGetValue(parameter.Name, out string? raw);
            string value = raw?.Trim() ?? string.Empty;
            if (parameter.Required && value.Length == 0)
                return SkillExpansionResult.Failure(
                    $"Parameter '{parameter.Name}' wajib diisi.");
            if (value.Length > parameter.MaxLength || value.Any(char.IsControl))
                return SkillExpansionResult.Failure(
                    $"Parameter '{parameter.Name}' tidak valid.");
            total += value.Length;
            if (total > AssistantSkillPolicy.MaxParameterTotalLength)
                return SkillExpansionResult.Failure("Total parameter workflow terlalu panjang.");
            values[parameter.Name] = value;
        }
        return ExpandSteps(values);
    }

    private SkillExpansionResult ExpandSteps(IReadOnlyDictionary<string, string> values)
    {
        string[] commands = _steps.ToArray();
        for (int index = 0; index < commands.Length; index++)
            foreach ((string name, string value) in values)
                commands[index] = commands[index].Replace(
                    $"{{{name}}}", value, StringComparison.OrdinalIgnoreCase);

        AssistantPlanStep[] steps = commands.Select((command, index) =>
            new AssistantPlanStep(index, command)).ToArray();
        return SkillExpansionResult.Expanded(
            new AssistantPlan(Array.AsReadOnly(steps)), $"Skill {DisplayName} siap.");
    }
}
