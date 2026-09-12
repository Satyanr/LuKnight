namespace LuKnight.Assistant;

public enum MemoryCommandKind
{
    Remember,
    Forget
}

public sealed record MemoryCommand(
    MemoryCommandKind Kind,
    string Text);

public static class MemoryCommandParser
{
    private static readonly string[] RememberPrefixes =
    [
        "ingat bahwa ",
        "ingat ini: ",
        "tolong ingat bahwa ",
        "simpan ke memori: ",
        "remember that ",
        "remember: "
    ];

    private static readonly string[] ForgetPrefixes =
    [
        "lupakan: ",
        "hapus memori: ",
        "forget that ",
        "forget: "
    ];

    public static MemoryCommand? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string text = input.Trim();
        foreach (string prefix in RememberPrefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string payload = text[prefix.Length..].Trim();
            return payload.Length == 0 ? null : new MemoryCommand(MemoryCommandKind.Remember, payload);
        }

        foreach (string prefix in ForgetPrefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string payload = text[prefix.Length..].Trim();
            return payload.Length == 0 ? null : new MemoryCommand(MemoryCommandKind.Forget, payload);
        }

        return null;
    }
}
