using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class ListApplicationsTool : IAssistantTool
{
    private readonly Func<bool> _enabled;
    private readonly Func<DesktopApplicationSnapshot> _capture;

    public string Name => BuiltInToolNames.DesktopListApplications;

    public ListApplicationsTool(
        Func<bool> enabled,
        Func<DesktopApplicationSnapshot>? capture = null)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _capture = capture ?? (() => DesktopApplicationAwarenessService.Capture());
    }

    public Task<ToolExecutionResult> ExecuteAsync(
        ToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_enabled())
        {
            return Task.FromResult(new ToolExecutionResult(
                false,
                "Application awareness sedang nonaktif. Aktifkan melalui Settings → AI & Chat."));
        }

        DesktopApplicationSnapshot snapshot = _capture();

        if (!snapshot.HasApplications)
        {
            return Task.FromResult(new ToolExecutionResult(
                true,
                "Aku tidak menemukan aplikasi desktop lain yang terlihat."));
        }

        string primary = snapshot.Primary is DesktopApplicationContext app
            ? DesktopApplicationAwarenessService.Format(app)
            : "Unknown";

        string visible = string.Join(
            ", ",
            snapshot.Applications.Select(DesktopApplicationAwarenessService.Format));

        return Task.FromResult(new ToolExecutionResult(
            true,
            $"Aplikasi utama yang terdeteksi: {primary}. Aplikasi terlihat: {visible}."));
    }
}
