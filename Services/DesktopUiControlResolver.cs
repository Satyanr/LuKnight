namespace LuKnight.Services;

public sealed record DesktopUiControlResolution(
    DesktopUiNodeSnapshot? Match,
    IReadOnlyList<DesktopUiNodeSnapshot> Alternatives)
{
    public bool Found => Match is not null;
    public bool Ambiguous => Match is null && Alternatives.Count > 1;
}

public static class DesktopUiControlResolver
{
    public static DesktopUiControlResolution Resolve(
        DesktopUiSnapshot snapshot,
        string query,
        string? controlType = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        string normalizedQuery = Normalize(query);
        string normalizedType = NormalizeType(controlType);
        IEnumerable<DesktopUiNodeSnapshot> source = Filter(snapshot, normalizedType);

        if (normalizedQuery.Length == 0)
        {
            return new(null, Order(source).Take(20).ToArray());
        }

        if (normalizedQuery.Length > 120)
            return new(null, Array.Empty<DesktopUiNodeSnapshot>());

        string[] tokens = normalizedQuery.Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries);
        var scored = source
            .Select(node => new
            {
                Node = node,
                Score = Score(node, normalizedQuery, tokens)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Node.IsOffscreen)
            .ThenBy(x => x.Node.Depth)
            .ThenBy(x => x.Node.Path, StringComparer.Ordinal)
            .ToArray();

        if (scored.Length == 0)
            return new(null, Array.Empty<DesktopUiNodeSnapshot>());

        int bestScore = scored[0].Score;
        DesktopUiNodeSnapshot[] best = scored
            .Where(x => x.Score == bestScore)
            .Select(x => x.Node)
            .ToArray();

        return best.Length == 1
            ? new(best[0], best)
            : new(null, best);
    }

    public static IReadOnlyList<DesktopUiNodeSnapshot> List(
        DesktopUiSnapshot snapshot,
        string? controlType = null,
        int maximum = 12)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (maximum is < 1 or > 50)
            throw new ArgumentOutOfRangeException(nameof(maximum));

        return Order(Filter(snapshot, NormalizeType(controlType)))
            .Take(maximum)
            .ToArray();
    }

    private static IEnumerable<DesktopUiNodeSnapshot> Filter(
        DesktopUiSnapshot snapshot,
        string normalizedType)
    {
        IEnumerable<DesktopUiNodeSnapshot> nodes = snapshot.Nodes
            .Where(x => !x.IsProtected)
            .Where(x => DesktopUiControlTypes.IsInteractive(x.ControlType));

        return normalizedType.Length == 0
            ? nodes
            : nodes.Where(x => TypeMatches(x.ControlType, normalizedType));
    }

    private static IOrderedEnumerable<DesktopUiNodeSnapshot> Order(
        IEnumerable<DesktopUiNodeSnapshot> nodes) =>
        nodes.OrderBy(x => x.IsOffscreen)
            .ThenBy(x => x.Depth)
            .ThenBy(x => x.Path, StringComparer.Ordinal);

    private static int Score(
        DesktopUiNodeSnapshot node,
        string query,
        IReadOnlyList<string> tokens)
    {
        string name = Normalize(node.Name);
        string automationId = Normalize(node.AutomationId);
        string className = Normalize(node.ClassName);

        if (name == query) return 100;
        if (automationId == query) return 95;
        if (name.StartsWith(query, StringComparison.Ordinal)) return 90;
        if (name.Contains(query, StringComparison.Ordinal)) return 80;
        if (automationId.Contains(query, StringComparison.Ordinal)) return 70;
        if (tokens.Count > 0 && tokens.All(token =>
                name.Contains(token, StringComparison.Ordinal))) return 60;
        if (className.Contains(query, StringComparison.Ordinal)) return 40;
        return 0;
    }

    private static bool TypeMatches(string actual, string requested)
    {
        if (string.Equals(actual, requested, StringComparison.OrdinalIgnoreCase))
            return true;

        return requested switch
        {
            "Menu" => actual is "Menu" or "MenuItem",
            "List" => actual is "List" or "ListItem",
            "Tree" => actual is "Tree" or "TreeItem",
            "Tab" => actual is "Tab" or "TabItem",
            _ => false
        };
    }

    public static string NormalizeType(string? value)
    {
        string normalized = Normalize(value);
        return normalized switch
        {
            "button" or "tombol" => "Button",
            "textbox" or "text box" or "input" or "edit" or "kolom" => "Edit",
            "menu" or "menuitem" or "menu item" => "Menu",
            "list" or "daftar" => "List",
            "tab" => "Tab",
            "combobox" or "combo box" or "dropdown" => "ComboBox",
            "checkbox" or "check box" => "CheckBox",
            "radiobutton" or "radio button" => "RadioButton",
            "tree" => "Tree",
            "link" or "hyperlink" => "Hyperlink",
            _ => string.Empty
        };
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        return string.Join(
            ' ',
            value.Trim().ToLowerInvariant().Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries));
    }
}
