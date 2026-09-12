using LuKnight.Services;

namespace LuKnight.Assistant;

public enum DesktopActionCommandKind
{
    Open,
    Focus
}

public sealed record DesktopActionCommand(
    DesktopActionCommandKind Kind,
    string AppId);

public static class DesktopActionCommandParser
{
    private static readonly string[] OpenPrefixes =
    [
        "buka ",
        "buka aplikasi ",
        "open ",
        "open app "
    ];

    private static readonly string[] FocusPrefixes =
    [
        "fokus ",
        "fokus ke ",
        "focus ",
        "focus app ",
        "pindah ke "
    ];

    public static DesktopActionCommand? Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        string text = input.Trim();

        DesktopActionCommand? open = Parse(text, OpenPrefixes, DesktopActionCommandKind.Open);
        if (open is not null)
            return open;

        return Parse(text, FocusPrefixes, DesktopActionCommandKind.Focus);
    }

    private static DesktopActionCommand? Parse(string text, IEnumerable<string> prefixes, DesktopActionCommandKind kind)
    {
        foreach (string prefix in prefixes)
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string target = text[prefix.Length..].Trim();

            if (!DesktopAppCatalog.TryResolve(target, out DesktopAppTarget app))
                return null;

            return new DesktopActionCommand(kind, app.Id);
        }

        return null;
    }
}
