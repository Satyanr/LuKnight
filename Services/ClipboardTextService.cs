using System.Runtime.InteropServices;
using System.Windows;

namespace LuKnight.Services;

public sealed record ClipboardTextSnapshot(
    bool HasText,
    string Text);

public static class ClipboardTextService
{
    public static ClipboardTextSnapshot Capture()
    {
        try
        {
            if (Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            {
                return dispatcher.Invoke(CaptureCore);
            }

            return CaptureCore();
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            return new ClipboardTextSnapshot(false, string.Empty);
        }
    }

    private static ClipboardTextSnapshot CaptureCore()
    {
        if (!Clipboard.ContainsText())
        {
            return new ClipboardTextSnapshot(false, string.Empty);
        }

        string text = Clipboard.GetText(TextDataFormat.UnicodeText);

        return new ClipboardTextSnapshot(
            !string.IsNullOrWhiteSpace(text),
            text ?? string.Empty);
    }
}
