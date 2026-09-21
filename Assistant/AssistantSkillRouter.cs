using System.Text.RegularExpressions;

namespace LuKnight.Assistant;

public sealed class AssistantSkillRouter
{
    private static readonly Regex ValidId = new(
        @"\A[a-z0-9][a-z0-9._-]{0,63}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    public void Register(IAssistantSkill skill)
    {
        ArgumentNullException.ThrowIfNull(skill);
        string id = skill.Id.Trim();
        if (!ValidId.IsMatch(id))
            throw new ArgumentException($"Skill id '{skill.Id}' tidak valid.", nameof(skill));
        if (!_skills.TryAdd(id, skill))
            throw new InvalidOperationException($"Skill '{id}' sudah terdaftar.");
        RegisterAlias(id, id);
        foreach (string alias in skill.Aliases) RegisterAlias(alias, id);
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

    private void RegisterAlias(string alias, string id)
    {
        string value = alias.Trim();
        if (!ValidId.IsMatch(value))
            throw new ArgumentException($"Skill alias '{alias}' tidak valid.");
        if (_aliases.TryGetValue(value, out string? existing) &&
            !string.Equals(existing, id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Skill alias '{value}' sudah digunakan.");
        _aliases[value] = id;
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
            new AssistantPlan(normalized.AsReadOnly()), result.Message);
    }
}
