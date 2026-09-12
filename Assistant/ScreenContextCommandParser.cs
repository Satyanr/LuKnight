namespace LuKnight.Assistant;

public sealed record ScreenContextCommand;

public static class ScreenContextCommandParser
{
    private static readonly string[] Phrases =
    [
        "lihat layar saya",
        "baca layar saya",
        "analisa layar saya",
        "analisis layar saya",
        "jelaskan layar saya",
        "apa yang ada di layar saya",
        "lihat screen saya",
        "look at my screen",
        "read my screen",
        "analyze my screen",
        "describe my screen",
        "what is on my screen"
    ];

    public static ScreenContextCommand? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string value = input.Trim().ToLowerInvariant();
        return Phrases.Any(phrase =>
            value.Equals(phrase, StringComparison.Ordinal) ||
            value.StartsWith(phrase + " ", StringComparison.Ordinal) ||
            value.StartsWith(phrase + "?", StringComparison.Ordinal))
            ? new ScreenContextCommand()
            : null;
    }
}
