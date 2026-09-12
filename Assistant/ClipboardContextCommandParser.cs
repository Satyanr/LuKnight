namespace LuKnight.Assistant;

public sealed record ClipboardContextCommand;

public static class ClipboardContextCommandParser
{
    private static readonly string[] Phrases =
    [
        "baca clipboard",
        "lihat clipboard",
        "ringkas clipboard",
        "jelaskan clipboard",
        "apa isi clipboard",
        "read clipboard",
        "summarize clipboard",
        "explain clipboard",
        "what is in my clipboard"
    ];

    public static ClipboardContextCommand? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        string value = input.Trim().ToLowerInvariant();

        return Phrases.Any(phrase =>
            value.Equals(phrase, StringComparison.Ordinal) ||
            value.StartsWith(phrase + " ", StringComparison.Ordinal))
            ? new ClipboardContextCommand()
            : null;
    }
}
