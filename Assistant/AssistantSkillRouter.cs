namespace LuKnight.Assistant;

public sealed class AssistantSkillRouter
{
    private readonly Dictionary<string, IAssistantSkill> _skills =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _aliases =
        new(StringComparer.OrdinalIgnoreCase);

    public AssistantSkillRouter(IEnumerable<IAssistantSkill>? skills = null)
    {
        if (skills is null) return;
        foreach (IAssistantSkill skill in skills) Register(skill);
    }

    public IReadOnlyCollection<string> RegisteredSkills => _skills.Keys.ToArray();

    public IReadOnlyList<AssistantSkillDescriptor> Catalog =>
        _skills.Values
            .OrderBy(skill => skill.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(skill => new AssistantSkillDescriptor(
                skill.Id,
                skill.DisplayName,
                skill.Description,
                skill.Aliases.ToArray()))
            .ToArray();

    public void Register(IAssistantSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        string id = skill.Id.Trim();
        if (!AssistantSkillPolicy.IsValidId(id))
            throw new ArgumentException($"Skill id '{skill.Id}' tidak valid.", nameof(skill));
        if (string.IsNullOrWhiteSpace(skill.DisplayName))
            throw new ArgumentException("Display name skill tidak boleh kosong.", nameof(skill));
        if (string.IsNullOrWhiteSpace(skill.Description))
            throw new ArgumentException("Description skill tidak boleh kosong.", nameof(skill));
        if (_skills.ContainsKey(id))
            throw new InvalidOperationException($"Skill '{id}' sudah terdaftar.");

        IReadOnlyList<string> rawAliases = skill.Aliases ??
            throw new ArgumentException("Alias collection skill tidak boleh null.", nameof(skill));
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { id };
        foreach (string rawAlias in rawAliases)
        {
            string alias = rawAlias?.Trim() ?? string.Empty;
            if (!AssistantSkillPolicy.IsValidId(alias))
                throw new ArgumentException($"Skill alias '{rawAlias}' tidak valid.", nameof(skill));
            aliases.Add(alias);
        }

        foreach (string alias in aliases)
        {
            if (_aliases.TryGetValue(alias, out string? existing) &&
                !string.Equals(existing, id, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Skill alias '{alias}' sudah digunakan oleh '{existing}'.");
        }

        _skills.Add(id, skill);
        foreach (string alias in aliases) _aliases[alias] = id;
    }

    public SkillExpansionResult Expand(SkillInvocation invocation)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        string key = invocation.Name.Trim();
        if (!_aliases.TryGetValue(key, out string? id) ||
            !_skills.TryGetValue(id, out IAssistantSkill? skill))
            return SkillExpansionResult.Failure($"Skill \"{key}\" tidak ditemukan.");

        SkillExpansionResult result = skill.Expand(invocation);
        if (!result.Success || result.Plan is null) return result;
        return NormalizePlan(result);
    }

    private static SkillExpansionResult NormalizePlan(SkillExpansionResult result)
    {
        AssistantPlan plan = result.Plan!;
        if (plan.Count is < 1 or > LocalMultiStepPlanParser.MaxSteps)
            return SkillExpansionResult.Failure(
                $"Skill harus menghasilkan 1 sampai {LocalMultiStepPlanParser.MaxSteps} langkah.");

        var normalized = new List<AssistantPlanStep>(plan.Count);
        int totalLength = 0;
        for (int index = 0; index < plan.Count; index++)
        {
            string command = plan.Steps[index].Command?.Trim() ?? string.Empty;
            if (command.Length == 0)
                return SkillExpansionResult.Failure($"Skill menghasilkan langkah {index + 1} yang kosong.");
            if (command.Length > LocalMultiStepPlanParser.MaxStepLength)
                return SkillExpansionResult.Failure($"Langkah skill {index + 1} terlalu panjang.");
            totalLength += command.Length;
            if (totalLength > LocalMultiStepPlanParser.MaxInputLength)
                return SkillExpansionResult.Failure("Total perintah skill terlalu panjang.");
            normalized.Add(new AssistantPlanStep(index, command));
        }

        return SkillExpansionResult.Expanded(
            new AssistantPlan(
                normalized.AsReadOnly(),
                plan.AllowRuntimeVariables),
            result.Message);
    }
}
