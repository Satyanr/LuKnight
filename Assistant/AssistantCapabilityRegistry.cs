namespace LuKnight.Assistant;


public enum AssistantCapabilityKind
{
    Feature,
    Skill,
    Action,
    Tool
}


public sealed record AssistantCapabilityDescriptor(
    string Id,
    AssistantCapabilityKind Kind,
    string DisplayName,
    string Description,
    IReadOnlyList<string> Aliases);


public sealed class AssistantCapabilityRegistry
{
    private readonly Dictionary<
        string,
        AssistantCapabilityDescriptor>
        _capabilities =
            new(
                StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<
        string,
        string>
        _aliases =
            new(
                StringComparer.OrdinalIgnoreCase);


    public AssistantCapabilityRegistry(
        IEnumerable<AssistantCapabilityDescriptor>?
            capabilities = null)
    {
        if (capabilities is null)
            return;

        foreach (AssistantCapabilityDescriptor capability
                 in capabilities)
        {
            Register(
                capability);
        }
    }


    public IReadOnlyList<AssistantCapabilityDescriptor>
        Catalog =>
        _capabilities.Values
            .OrderBy(
                item =>
                    item.Kind)
            .ThenBy(
                item =>
                    item.DisplayName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();


    public bool Contains(
        string id)
    {
        string key =
            id?.Trim() ??
            string.Empty;

        return _capabilities
            .ContainsKey(
                key);
    }


    public bool TryResolve(
        string name,
        out AssistantCapabilityDescriptor capability)
    {
        string key =
            name?.Trim() ??
            string.Empty;


        if (_capabilities.TryGetValue(
                key,
                out capability!))
        {
            return true;
        }


        if (_aliases.TryGetValue(
                key,
                out string? id) &&
            _capabilities.TryGetValue(
                id,
                out capability!))
        {
            return true;
        }


        capability =
            default!;

        return false;
    }


    private void Register(
        AssistantCapabilityDescriptor source)
    {
        ArgumentNullException.ThrowIfNull(
            source);


        string id =
            source.Id?.Trim() ??
            string.Empty;

        string display =
            source.DisplayName?.Trim() ??
            string.Empty;

        string description =
            source.Description?.Trim() ??
            string.Empty;


        if (!IsSafeName(
                id,
                120) ||
            !IsSafeName(
                display,
                120) ||
            !IsSafeName(
                description,
                500))
        {
            throw new ArgumentException(
                "Capability metadata tidak valid.",
                nameof(source));
        }


        string[] aliases =
            (source.Aliases ??
             Array.Empty<string>())
            .Select(
                item =>
                    item?.Trim() ??
                    string.Empty)
            .Where(
                item =>
                    item.Length > 0)
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();


        foreach (string alias
                 in aliases)
        {
            if (!IsSafeName(
                    alias,
                    100))
            {
                throw new ArgumentException(
                    $"Capability alias '{alias}' tidak valid.",
                    nameof(source));
            }
        }


        var frozen =
            source with
            {
                Id =
                    id,

                DisplayName =
                    display,

                Description =
                    description,

                Aliases =
                    Array.AsReadOnly(
                        aliases)
            };


        if (!_capabilities.TryAdd(
                id,
                frozen))
        {
            throw new InvalidOperationException(
                $"Capability '{id}' sudah terdaftar.");
        }


        foreach (string alias
                 in aliases)
        {
            if (_aliases.TryGetValue(
                    alias,
                    out string? existing) &&
                !string.Equals(
                    existing,
                    id,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Capability alias '{alias}' ambigu.");
            }

            _aliases[alias] =
                id;
        }
    }


    private static bool IsSafeName(
        string value,
        int maxLength) =>
        value.Length is > 0 &&
        value.Length <= maxLength &&
        !value.Any(
            char.IsControl);


    public static AssistantCapabilityRegistry
        Create(
            AssistantSkillRouter skills,
            AssistantActionRouter actions,
            AssistantToolRouter tools)
    {
        ArgumentNullException.ThrowIfNull(
            skills);

        ArgumentNullException.ThrowIfNull(
            actions);

        ArgumentNullException.ThrowIfNull(
            tools);


        var result =
            new List<
                AssistantCapabilityDescriptor>
            {
                new(
                    "feature:chat",
                    AssistantCapabilityKind.Feature,
                    "Assistant Chat",
                    "Percakapan dan bantuan berbasis konteks yang diberikan user.",
                    []),

                new(
                    "feature:scheduler",
                    AssistantCapabilityKind.Feature,
                    "Scheduler",
                    "Reminder dan workflow terjadwal lokal.",
                    []),

                new(
                    "feature:companion",
                    AssistantCapabilityKind.Feature,
                    "Proactive Companion",
                    "Saran lokal berbasis broad application context.",
                    [])
            };


        foreach (AssistantSkillDescriptor skill
                 in skills.Catalog)
        {
            string[] aliases =
                skill.Aliases
                    .Append(
                        skill.Id)
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray();

            result.Add(
                new(
                    $"skill:{skill.Id}",
                    AssistantCapabilityKind.Skill,
                    skill.DisplayName,
                    skill.Description,
                    aliases));
        }


        foreach (string action
                 in actions.RegisteredActions)
        {
            result.Add(
                new(
                    $"action:{action}",
                    AssistantCapabilityKind.Action,
                    action,
                    "Registered local assistant action.",
                    []));
        }


        foreach (string tool
                 in tools.RegisteredTools)
        {
            result.Add(
                new(
                    $"tool:{tool}",
                    AssistantCapabilityKind.Tool,
                    tool,
                    "Registered local assistant tool.",
                    []));
        }


        return new AssistantCapabilityRegistry(
            result);
    }
}
