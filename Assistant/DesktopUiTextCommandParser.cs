using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed record DesktopUiTextCommand(
    string WindowQuery,
    string ControlQuery,
    string Value);

public static class
    DesktopUiTextCommandParser
{
    private static readonly string[]
        Prefixes =
        [
            "isi textbox ",
            "isi text box ",
            "isi kolom ",
            "set textbox ",
            "set text box "
        ];

    public static DesktopUiTextCommand?
        Parse(
            string input)
    {
        if (string.IsNullOrWhiteSpace(
                input))
        {
            return null;
        }

        string text =
            input.Trim();

        string? remainder =
            StripPrefix(
                text);

        if (remainder is null)
            return null;

        int windowSplit =
            remainder.LastIndexOf(
                " di window ",
                StringComparison.OrdinalIgnoreCase);

        if (windowSplit <= 0)
            return null;

        string beforeWindow =
            remainder[..windowSplit];

        string window =
            remainder[
                (windowSplit +
                 " di window ".Length)..]
                .Trim();

        int valueSplit =
            beforeWindow.IndexOf(
                " dengan ",
                StringComparison.OrdinalIgnoreCase);

        if (valueSplit <= 0)
            return null;

        string control =
            beforeWindow[..valueSplit]
                .Trim();

        string value =
            beforeWindow[
                (valueSplit +
                 " dengan ".Length)..];

        if (control.Length is
                < 1 or > 120 ||
            window.Length is
                < 1 or > 200 ||
            !DesktopUiTextInputPolicy
                .ValidateValue(
                    value,
                    out _))
        {
            return null;
        }

        if (window.Equals(
                "aktif",
                StringComparison.OrdinalIgnoreCase) ||
            window.Equals(
                "ini",
                StringComparison.OrdinalIgnoreCase) ||
            window.Equals(
                "saat ini",
                StringComparison.OrdinalIgnoreCase) ||
            window.Equals(
                "current",
                StringComparison.OrdinalIgnoreCase))
        {
            window =
                "window aktif";
        }

        return new DesktopUiTextCommand(
            window,
            control.ToLowerInvariant(),
            value);
    }

    public static bool IsTextInputCommand(string input) =>
        !string.IsNullOrWhiteSpace(input) && StripPrefix(input.TrimStart()) is not null;

    private static string?
        StripPrefix(
            string text)
    {
        foreach (string prefix
                 in Prefixes.OrderByDescending(
                     x => x.Length))
        {
            if (text.StartsWith(
                    prefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                return text[
                    prefix.Length..];
            }
        }

        return null;
    }
}
