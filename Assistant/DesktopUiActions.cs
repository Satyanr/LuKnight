using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class InvokeDesktopUiControlAction : IAssistantAction
{
    private readonly Func<bool> _enabled;
    private readonly IDesktopWindowTargetCatalog _windows;
    private readonly IDesktopUiAutomationReader _ui;
    private readonly IDesktopUiActionExecutor _executor;

    private readonly IDesktopMouseActionExecutor? _mouseFallback;

    private readonly IDesktopUiAssistedResolver? _assistedResolver;

    public string Name => BuiltInActionNames.DesktopInvokeUiControl;

    public InvokeDesktopUiControlAction(
        Func<bool> enabled,
        IDesktopWindowTargetCatalog windows,
        IDesktopUiAutomationReader ui,
        IDesktopUiActionExecutor executor,
        IDesktopMouseActionExecutor? mouseFallback = null,
        IDesktopUiAssistedResolver? assistedResolver = null)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _mouseFallback = mouseFallback;
        _assistedResolver = assistedResolver;
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
            DesktopUiControlResolver.Resolve(
                snapshot,
                query,
                "Button");

        bool screenAssisted =
            false;

        if (control.Ambiguous &&
            _assistedResolver is not null)
        {
            DesktopUiAssistedResolution assisted =
                await _assistedResolver.ResolveAsync(
                    window,
                    control,
                    cancellationToken);

            control =
                assisted.Resolution;

            screenAssisted =
                assisted.UsedScreenEvidence;
        }
        if (control.Ambiguous)
            return new(false, "Ada beberapa tombol yang cocok. Gunakan nama yang lebih spesifik.");
        if (!control.Found || control.Match is null)
            return new(false, $"Tombol \"{query}\" tidak ditemukan.");

        DesktopUiNodeSnapshot button = control.Match;
        if (DesktopUiActionPolicy.IsTemporarilyBlocked(button, out string policyReason))
            return new(false, policyReason);

        string screenNotice =
            screenAssisted
                ? " Target dipilih dari kandidat UIA yang ambigu melalui validasi layar lokal; screenshot tidak dikirim ke AI."
                : string.Empty;

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
            $"Izinkan Lu-Knight menekan tombol {button.DisplayName} pada window {window.DisplayLabel}? " +
            "UI Automation akan diprioritaskan; jika tombol tidak menyediakan InvokePattern, " +
            "Lu-Knight boleh menggunakan klik mouse tervalidasi pada tombol yang sama." + screenNotice,
            IncludeInContext: false,
            Risk: AssistantActionRisk.Interaction);
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
        if (current is null || !string.Equals(
                DesktopUiNodeIdentity.Fingerprint(current),
                fingerprint,
                StringComparison.Ordinal))
        {
            return new(false, "Tombol berubah sejak konfirmasi. Ulangi perintah.");
        }

        if (DesktopUiActionPolicy.IsTemporarilyBlocked(current, out string policyReason))
            return new(false, policyReason);

        DesktopUiInvokeResult invoke = await _executor.InvokeAsync(
            window,
            path,
            fingerprint,
            cancellationToken);
        if (invoke.Success)
            return new(true, invoke.Message);
        if (!invoke.CanMouseFallback)
            return new(false, invoke.Message);
        if (_mouseFallback is null)
            return new(false, "Tombol tidak mendukung UI Automation Invoke dan mouse fallback tidak tersedia.");

        cancellationToken.ThrowIfCancellationRequested();
        DesktopActionResult mouse = await _mouseFallback.ClickAsync(
            window,
            path,
            fingerprint,
            cancellationToken);
        if (!mouse.Success)
            return new(false, $"UI Automation Invoke tidak tersedia. Mouse fallback juga dibatalkan: {mouse.Message}");

        return new(true, $"{mouse.Message} UI Automation Invoke tidak tersedia, jadi digunakan mouse fallback tervalidasi.");
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
