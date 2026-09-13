using System.Windows.Automation;

namespace LuKnight.Services;

public static class DesktopUiAutomationLocator
{
    private const int MaxDepth = 12;
    private const int MaxSiblingIndex = 1000;

    public static AutomationElement? ResolvePath(AutomationElement root, string path)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (string.IsNullOrWhiteSpace(path))
            return null;

        string[] parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] != "0" || parts.Length - 1 > MaxDepth)
            return null;

        AutomationElement current = root;
        TreeWalker walker = TreeWalker.ControlViewWalker;
        for (int level = 1; level < parts.Length; level++)
        {
            if (!int.TryParse(parts[level], out int index) ||
                index < 0 ||
                index > MaxSiblingIndex)
            {
                return null;
            }

            AutomationElement? child = walker.GetFirstChild(current);
            for (int i = 0; i < index && child is not null; i++)
                child = walker.GetNextSibling(child);
            if (child is null)
                return null;
            current = child;
        }

        return current;
    }
}
