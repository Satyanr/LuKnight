namespace LuKnight.Assistant;

public static class SkillCommandParser
{
    private static readonly string[] Prefixes = ["jalankan skill", "run skill"];
    private static readonly string[] NamedParameterPrefixes =
        ["dengan parameter ", "with parameters "];
    private static readonly string[] CatalogCommands =
    [
        "daftar skill",
        "lihat skill",
        "skill apa saja",
        "skill apa yang tersedia",
        "list skills",
        "show skills"
    ];

    public static bool IsCatalogCommand(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        string text = input.Trim();
        return CatalogCommands.Any(command =>
            string.Equals(text, command, StringComparison.OrdinalIgnoreCase));
    }

    public static bool LooksLikeSkillCommand(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return false;
        string text = input.Trim();
        return Prefixes.Any(prefix =>
            text.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase));
    }

    public static SkillInvocation? Parse(string? input)
    {
        if (!LooksLikeSkillCommand(input)) return null;
        string text = input!.Trim();
        string prefix = Prefixes.First(candidate =>
            text.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith(candidate + " ", StringComparison.OrdinalIgnoreCase));
        string rest = text[prefix.Length..].Trim();
        if (rest.Length == 0) return null;

        int space = rest.IndexOf(' ');
        string name = space < 0 ? rest : rest[..space];
        string tail = space < 0 ? string.Empty : rest[(space + 1)..].Trim();
        string argument = string.Empty;
        if (tail.Length > 0)
        {
            string? namedPrefix = NamedParameterPrefixes.FirstOrDefault(value =>
                tail.StartsWith(value, StringComparison.OrdinalIgnoreCase));
            if (namedPrefix is not null)
            {
                IReadOnlyDictionary<string, string>? parameters =
                    ParseNamedParameters(tail[namedPrefix.Length..].Trim());
                if (parameters is null) return null;
                return new SkillInvocation(name, Parameters: parameters);
            }
            string[] argumentPrefixes = ["dengan ", "with "];
            string? argumentPrefix = argumentPrefixes.FirstOrDefault(value =>
                tail.StartsWith(value, StringComparison.OrdinalIgnoreCase));
            if (argumentPrefix is null) return null;
            argument = tail[argumentPrefix.Length..].Trim();
            if (argument.Length == 0 || argument.Length > 500 || argument.Any(char.IsControl))
                return null;
        }

        return new SkillInvocation(name, argument, IncludeInContext: false);
    }

    private static IReadOnlyDictionary<string, string>? ParseNamedParameters(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        List<string>? parts = SplitParameterAssignments(input);
        if (parts is null || parts.Count is < 1 or > AssistantSkillPolicy.MaxParameters)
            return null;
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        foreach (string part in parts)
        {
            int equals = part.IndexOf('=');
            if (equals <= 0) return null;
            string name = part[..equals].Trim();
            string rawValue = part[(equals + 1)..].Trim();
            if (!AssistantSkillPolicy.IsValidParameterName(name) || result.ContainsKey(name))
                return null;
            string? value = ParseParameterValue(rawValue);
            if (value is null || value.Length > AssistantSkillPolicy.MaxParameterValueLength ||
                value.Any(char.IsControl)) return null;
            total += value.Length;
            if (total > AssistantSkillPolicy.MaxParameterTotalLength) return null;
            result.Add(name, value);
        }
        return new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(result);
    }

    private static List<string>? SplitParameterAssignments(string input)
    {
        var parts = new List<string>();
        int start = 0;
        char quote = '\0';
        bool escaped = false;
        for (int index = 0; index < input.Length; index++)
        {
            char current = input[index];
            if (escaped) { escaped = false; continue; }
            if (quote != '\0' && current == '\\') { escaped = true; continue; }
            if (current is '"' or '\'')
            {
                if (quote == '\0') quote = current;
                else if (quote == current) quote = '\0';
                continue;
            }
            if (current == ';' && quote == '\0')
            {
                string part = input[start..index].Trim();
                if (part.Length == 0) return null;
                parts.Add(part);
                start = index + 1;
            }
        }
        if (quote != '\0' || escaped) return null;
        string last = input[start..].Trim();
        if (last.Length == 0) return null;
        parts.Add(last);
        return parts;
    }

    private static string? ParseParameterValue(string raw)
    {
        if (raw.Length == 0) return null;
        if (raw[0] is not ('"' or '\''))
            return raw.Contains('"') || raw.Contains('\'') ? null : raw.Trim();
        char quote = raw[0];
        if (raw.Length < 2 || raw[^1] != quote) return null;
        string inner = raw[1..^1];
        var builder = new System.Text.StringBuilder();
        bool escaped = false;
        foreach (char current in inner)
        {
            if (escaped)
            {
                if (current != quote && current != '\\') return null;
                builder.Append(current);
                escaped = false;
            }
            else if (current == '\\') escaped = true;
            else builder.Append(current);
        }
        return escaped ? null : builder.ToString();
    }
}
