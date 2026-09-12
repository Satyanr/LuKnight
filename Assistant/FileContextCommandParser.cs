namespace LuKnight.Assistant;

public sealed record FileContextCommand(string Path);

public static class FileContextCommandParser
{
    private static readonly string[] Prefixes =
    [
        "baca file: ",
        "lihat file: ",
        "ringkas file: ",
        "jelaskan file: ",
        "read file: ",
        "summarize file: ",
        "explain file: "
    ];

    public static FileContextCommand? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string text = input.Trim();

        foreach (string prefix in Prefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string path = text[prefix.Length..].Trim().Trim('"');
            if (path.Length == 0)
                return null;

            return new FileContextCommand(path);
        }

        return null;
    }
}
