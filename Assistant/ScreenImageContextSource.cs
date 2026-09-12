using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class ScreenImageContextSource : IAssistantContextSource
{
    private readonly Func<bool> _enabled;
    private readonly Func<bool> _geminiAvailable;
    private readonly Func<ScreenCaptureSnapshot?> _capture;

    public string Name => BuiltInContextNames.ScreenImage;

    public ScreenImageContextSource(
        Func<bool> enabled,
        Func<bool> geminiAvailable,
        Func<ScreenCaptureSnapshot?>? capture = null)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _geminiAvailable = geminiAvailable ?? throw new ArgumentNullException(nameof(geminiAvailable));
        _capture = capture ?? ScreenCaptureService.CapturePrimaryDisplay;
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
                "Screen context sedang nonaktif. Aktifkan melalui Settings → AI & Chat."));
        }

        if (!_geminiAvailable())
        {
            return Task.FromResult(new ContextCaptureResult(
                false,
                "Screen context memerlukan Gemini aktif dan API key yang tersedia."));
        }

        ScreenCaptureSnapshot? snapshot = _capture();
        if (snapshot is null)
        {
            return Task.FromResult(new ContextCaptureResult(
                false,
                "Screenshot tidak dapat diambil saat ini."));
        }

        var reference = new ChatReferenceBlock(
            Kind: "screen-image",
            Name: "Primary display",
            Content: $"One-time primary display screenshot. Image size: {snapshot.Width}x{snapshot.Height}.",
            MimeType: snapshot.MimeType,
            Base64Data: snapshot.Base64Data);

        return Task.FromResult(new ContextCaptureResult(
            true,
            "Screenshot dimuat untuk request ini.",
            reference));
    }
}
