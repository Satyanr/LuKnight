namespace LuKnight.Services;

public static class DesktopUiText
{
    public const int DefaultMaxLength = 160;

    public static string Normalize(string? value, int maximum = DefaultMaxLength)
    {
        if (maximum < 1)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return normalized.Length <= maximum ? normalized : normalized[..maximum];
    }
}
