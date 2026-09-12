using System.Text.RegularExpressions;

namespace LuKnight.Services;

/// <summary>Converts common model Markdown to the plain text used by chat bubbles.</summary>
public static partial class AssistantTextFormatter
{
    public static string Format(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        // Code is literal: never strip operators, quotes or Windows path separators.
        var result = new System.Text.StringBuilder();
        int offset = 0;
        foreach (Match match in Code().Matches(text))
        {
            result.Append(FormatProse(text[offset..match.Index]));
            result.Append(match.Groups["fenced"].Success
                ? match.Groups["fenced"].Value
                : match.Groups["inline"].Value);
            offset = match.Index + match.Length;
        }
        result.Append(FormatProse(text[offset..]));
        return result.ToString().Trim();
    }

    private static string FormatProse(string text)
    {
        text = EscapedEmphasis().Replace(text, "${body}");
        text = text.Replace(@"\'", "'");
        text = Headings().Replace(text, "");
        text = Bullets().Replace(text, "$1• ");
        text = Emphasis().Replace(text, "$2");
        return text;
    }

    [GeneratedRegex(@"(?m)^[ \t]*```[^\r\n]*\r?\n(?<fenced>[\s\S]*?)^[ \t]*```[ \t]*\r?$|`(?<inline>[^`\r\n]+)`")]
    private static partial Regex Code();

    [GeneratedRegex(@"(?m)^[ \t]{0,3}#{1,6}[ \t]+")]
    private static partial Regex Headings();

    [GeneratedRegex(@"(?m)^([ \t]*)\\?\*[ \t]+")]
    private static partial Regex Bullets();

    [GeneratedRegex(@"(?<![\w*\\/])(\*{1,3})(?=\S)([^\r\n]*?\S)\1(?![\w*])")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"(?<![\w\\/:])(?<mark>(?:\\\*){1,3})(?=\S)(?<body>[^\r\n]+?)\k<mark>(?![\w*])")]
    private static partial Regex EscapedEmphasis();
}
