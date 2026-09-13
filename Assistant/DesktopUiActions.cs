using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class InvokeDesktopUiControlAction : IAssistantAction
{
    private readonly Func<bool> _enabled;
    private readonly IDesktopWindowTargetCatalog _windows;
    private readonly IDesktopUiAutomationReader _ui;
    private readonly IDesktopUiActionExecutor _executor;

    public string Name => BuiltInActionNames.DesktopInvokeUiControl;

    public InvokeDesktopUiControlAction(
        Func<bool> enabled,
        IDesktopWindowTargetCatalog windows,
        IDesktopUiAutomationReader ui,
        IDesktopUiActionExecutor executor)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public ActionPreparationResult Prepare(ActionInvocation invocation) =>
        new(false, "UI Automation action memerlukan async preparation.");

    public async Task<ActionPreparationResult> PrepareAsync(
        ActionInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_enabled())
            return new(false, "Desktop interaction sedang nonaktif.");
        if (!TryArg(invocation, "window", out string windowQuery) ||
            !TryArg(invocation, "query", out string query))
        {
            return new(false, "Target UI tidak lengkap.");
        }

        DesktopWindowResolution resolved = _windows.Resolve(windowQuery);
        if (resolved.Ambiguous)
            return new(false, "Window target ambigu. Sebutkan window yang lebih spesifik.");
        if (!resolved.Found || resolved.Match is null)
            return new(false, "Window target tidak ditemukan.");

        DesktopWindowTarget window = resolved.Match;
        DesktopUiSnapshot snapshot = await _ui.CaptureAsync(
            window,
            cancellationToken: cancellationToken);
        if (!snapshot.Success)
            return new(false, snapshot.Error ?? "UI Automation gagal membaca window.");

        DesktopUiControlResolution control =
            DesktopUiControlResolver.Resolve(snapshot, query, "Button");
        if (control.Ambiguous)
            return new(false, "Ada beberapa tombol yang cocok. Gunakan nama yang lebih spesifik.");
        if (!control.Found || control.Match is null)
            return new(false, $"Tombol \"{query}\" tidak ditemukan.");

        DesktopUiNodeSnapshot button = control.Match;
        if (button.IsProtected || !button.IsEnabled || button.IsOffscreen)
            return new(false, "Tombol target tidak tersedia untuk diaktifkan.");

        var prepared = new PreparedAssistantAction(
            Name,
            new Dictionary<string, string>
            {
                ["windowId"] = window.Id,
                ["windowFingerprint"] = window.Fingerprint,
                ["controlPath"] = button.Path,
                ["controlFingerprint"] = DesktopUiNodeIdentity.Fingerprint(button)
            },
            $"Tekan tombol {button.DisplayName}",
            $"Izinkan Lu-Knight menekan tombol {button.DisplayName} pada window {window.DisplayLabel}?",
            IncludeInContext: false);
        return new(true, $"Siap menekan tombol {button.DisplayName}.", prepared);
    }

    public async Task<ActionExecutionResult> ExecuteAsync(
        PreparedAssistantAction action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_enabled())
            return new(false, "Desktop interaction sedang nonaktif.");
        if (!action.Arguments.TryGetValue("windowId", out string? windowId) ||
            !action.Arguments.TryGetValue("windowFingerprint", out string? windowFingerprint) ||
            !action.Arguments.TryGetValue("controlPath", out string? path) ||
            !action.Arguments.TryGetValue("controlFingerprint", out string? fingerprint))
        {
            return new(false, "UI action target tidak valid.");
        }

        if (!_windows.TryResolveById(windowId, out DesktopWindowTarget window) ||
            !string.Equals(window.Fingerprint, windowFingerprint, StringComparison.Ordinal))
        {
            return new(false, "Window berubah sejak konfirmasi. Ulangi perintah.");
        }

        DesktopUiSnapshot snapshot = await _ui.CaptureAsync(
            window,
            cancellationToken: cancellationToken);
        if (!snapshot.Success)
            return new(false, snapshot.Error ?? "UI Automation gagal membaca ulang window.");

        DesktopUiNodeSnapshot? current = snapshot.Nodes.FirstOrDefault(x => x.Path == path);
        if (current is null ||
            current.IsProtected ||
            current.ControlType != "Button" ||
            !current.IsEnabled ||
            current.IsOffscreen ||
            !string.Equals(
                DesktopUiNodeIdentity.Fingerprint(current),
                fingerprint,
                StringComparison.Ordinal))
        {
            return new(false, "Tombol berubah sejak konfirmasi. Ulangi perintah.");
        }

        DesktopActionResult result = await _executor.InvokeAsync(
            window,
            path,
            fingerprint,
            cancellationToken);
        return new(result.Success, result.Message);
    }

    private static bool TryArg(ActionInvocation invocation, string key, out string value)
    {
        if (invocation.Arguments.TryGetValue(key, out string? found) &&
            !string.IsNullOrWhiteSpace(found))
        {
            value = found.Trim();
            return true;
        }

        value = string.Empty;
        return false;
    }
}
