using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class ClipboardTextContextSource : IAssistantContextSource
{
    private const int MaxContextChars = 24_000;

    private readonly Func<bool> _enabled;
    private readonly Func<ClipboardTextSnapshot> _capture;

    public string Name => BuiltInContextNames.ClipboardText;

    public ClipboardTextContextSource(
        Func<bool> enabled,
        Func<ClipboardTextSnapshot>? capture = null)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _capture = capture ?? ClipboardTextService.Capture;
    }

    public Task<ContextCaptureResult> CaptureAsync(
        ContextInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_enabled())
        {
            return Task.FromResult(new ContextCaptureResult(
                false,
                "Clipboard context sedang nonaktif. Aktifkan melalui Settings → AI & Chat."));
        }

        ClipboardTextSnapshot snapshot = _capture();

        if (!snapshot.HasText || string.IsNullOrWhiteSpace(snapshot.Text))
        {
            return Task.FromResult(new ContextCaptureResult(
                false,
                "Clipboard tidak berisi text yang dapat dibaca."));
        }

        string content = snapshot.Text;
        bool truncated = content.Length > MaxContextChars;
        if (truncated)
        {
            content = content[..MaxContextChars];
        }

        var reference = new ChatReferenceBlock(
            Kind: "clipboard-text",
            Name: "Clipboard",
            Content: content,
            Truncated: truncated);

        return Task.FromResult(new ContextCaptureResult(
            true,
            truncated ? "Clipboard text dimuat sebagian." : "Clipboard text dimuat.",
            reference));
    }
}
