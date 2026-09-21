using System.Text.RegularExpressions;

namespace LuKnight.Assistant;

public static class AssistantSkillPolicy
{
    private static readonly Regex ValidId = new(
        @"\A[a-z0-9][a-z0-9._-]{0,63}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public const int MaxFiles = 64;
    public const long MaxFileBytes = 64 * 1024;
    public const int MaxAliases = 8;
    public const int MaxDisplayNameLength = 100;
    public const int MaxDescriptionLength = 500;
    public static bool IsValidId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ValidId.IsMatch(value.Trim());
    public static bool HasControlCharacters(string value) => value.Any(char.IsControl);
}
