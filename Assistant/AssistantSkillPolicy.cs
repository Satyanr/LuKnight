using System.Text.RegularExpressions;

namespace LuKnight.Assistant;

public static class AssistantSkillPolicy
{
    private static readonly Regex ValidId = new(
        @"\A[a-z0-9][a-z0-9._-]{0,63}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ValidParameterName = new(
        @"\A[a-z][a-z0-9_-]{0,31}\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public const int MaxFiles = 64;
    public const long MaxFileBytes = 64 * 1024;
    public const int MaxAliases = 8;
    public const int MaxDisplayNameLength = 100;
    public const int MaxDescriptionLength = 500;
    public const int MaxParameters = 8;
    public const int MaxParameterValueLength = 500;
    public const int MaxParameterTotalLength = 1200;
    public static bool IsValidId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ValidId.IsMatch(value.Trim());
    public static bool HasControlCharacters(string value) => value.Any(char.IsControl);
    public static bool IsValidParameterName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && ValidParameterName.IsMatch(value.Trim());
    public static bool IsReservedParameterName(string value) =>
        string.Equals(value, "argument", StringComparison.OrdinalIgnoreCase);
}
