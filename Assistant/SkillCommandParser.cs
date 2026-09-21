namespace LuKnight.Assistant;

public static class SkillCommandParser
{
    private static readonly string[] Prefixes = ["jalankan skill", "run skill"];
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
}
