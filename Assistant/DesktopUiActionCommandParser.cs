namespace LuKnight.Assistant;

public sealed record DesktopUiActionCommand(
    string WindowQuery,
    string ControlType,
    string Query);

public static class DesktopUiActionCommandParser
{
    public static DesktopUiActionCommand? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string text = Normalize(input);
        string? remainder = ExtractPrefix(
            text,
            ["klik tombol ", "tekan tombol ", "press button ", "click button "]);
        if (remainder is null)
            return null;

        string marker = text.StartsWith("press button ", StringComparison.Ordinal) ||
                        text.StartsWith("click button ", StringComparison.Ordinal)
            ? " in window "
            : " di window ";
        int split = remainder.LastIndexOf(marker, StringComparison.Ordinal);
        if (split < 1)
            return null;

        string query = remainder[..split].Trim();
        string window = remainder[(split + marker.Length)..].Trim();
        if (query.Length == 0 || query.Length > 120 ||
            window.Length == 0 || window.Length > 200)
        {
            return null;
        }

        if (window is "ini" or "aktif" or "saat ini" or "current")
            window = "window aktif";

        return new DesktopUiActionCommand(window, "Button", query);
    }

    private static string? ExtractPrefix(string text, IEnumerable<string> prefixes)
    {
        foreach (string prefix in prefixes.OrderByDescending(x => x.Length))
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                return text[prefix.Length..].Trim();
        }

        return null;
    }

    private static string Normalize(string value) =>
        string.Join(
            ' ',
            value.Trim().TrimEnd('.', '?', '!', ',').ToLowerInvariant()
                .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
}
