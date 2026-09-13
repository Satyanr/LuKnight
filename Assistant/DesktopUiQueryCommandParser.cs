namespace LuKnight.Assistant;

public enum DesktopUiQueryMode
{
    Find,
    List
}

public sealed record DesktopUiQueryCommand(
    DesktopUiQueryMode Mode,
    string WindowQuery,
    string ControlType,
    string Query);

public static class DesktopUiQueryCommandParser
{
    private static readonly (string Phrase, string Type)[] TypePhrases =
    [
        ("tombol", "Button"),
        ("button", "Button"),
        ("textbox", "Edit"),
        ("text box", "Edit"),
        ("input", "Edit"),
        ("kolom", "Edit"),
        ("menu", "Menu"),
        ("dropdown", "ComboBox"),
        ("combo box", "ComboBox"),
        ("checkbox", "CheckBox"),
        ("check box", "CheckBox"),
        ("radio button", "RadioButton"),
        ("tab", "Tab"),
        ("link", "Hyperlink")
    ];

    public static DesktopUiQueryCommand? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string text = Normalize(input);
        if (TryList(text, out DesktopUiQueryCommand? list))
            return list;
        if (TryFind(text, out DesktopUiQueryCommand? find))
            return find;
        return null;
    }

    private static bool TryList(
        string text,
        out DesktopUiQueryCommand? command)
    {
        command = null;
        string? remainder = ExtractPrefix(
            text,
            ["ada ", "lihat ", "tampilkan ", "list "]);
        if (remainder is null)
            return false;

        foreach ((string phrase, string type) in TypePhrases
                     .OrderByDescending(x => x.Phrase.Length))
        {
            string[] patterns =
            [
                phrase + " apa di window ",
                phrase + " di window "
            ];

            foreach (string pattern in patterns)
            {
                if (!remainder.StartsWith(pattern, StringComparison.Ordinal))
                    continue;

                string window = remainder[pattern.Length..].Trim();
                if (window.Length == 0)
                    return false;

                command = new DesktopUiQueryCommand(
                    DesktopUiQueryMode.List,
                    NormalizeWindow(window),
                    type,
                    string.Empty);
                return true;
            }
        }

        return false;
    }

    private static bool TryFind(
        string text,
        out DesktopUiQueryCommand? command)
    {
        command = null;
        string? remainder = ExtractPrefix(
            text,
            ["cari ", "carikan ", "temukan ", "find "]);
        if (remainder is null)
            return false;

        foreach ((string phrase, string type) in TypePhrases
                     .OrderByDescending(x => x.Phrase.Length))
        {
            string prefix = phrase + " ";
            if (!remainder.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            string body = remainder[prefix.Length..];
            int marker = body.LastIndexOf(" di window ", StringComparison.Ordinal);
            if (marker < 1)
                return false;

            string query = body[..marker].Trim();
            string window = body[(marker + " di window ".Length)..].Trim();
            if (query.Length == 0 || window.Length == 0)
                return false;

            command = new DesktopUiQueryCommand(
                DesktopUiQueryMode.Find,
                NormalizeWindow(window),
                type,
                query);
            return true;
        }

        return false;
    }

    private static string NormalizeWindow(string value) =>
        value is "ini" or "saat ini" or "aktif" or "current"
            ? "window aktif"
            : value;

    private static string? ExtractPrefix(
        string text,
        IEnumerable<string> prefixes)
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
            value.Trim()
                .TrimEnd('.', '?', '!', ',')
                .ToLowerInvariant()
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries));
}
