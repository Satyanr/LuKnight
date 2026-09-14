using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class SetDesktopUiTextAction
    : IAssistantAction
{
    private readonly Func<bool> _enabled;
    private readonly IDesktopWindowTargetCatalog _windows;
    private readonly IDesktopUiAutomationReader _ui;
    private readonly IDesktopUiTextActionExecutor _executor;

    private readonly IDesktopKeyboardTextActionExecutor? _keyboardFallback;

    public string Name =>
        BuiltInActionNames.DesktopSetUiText;

    public SetDesktopUiTextAction(
        Func<bool> enabled,
        IDesktopWindowTargetCatalog windows,
        IDesktopUiAutomationReader ui,
        IDesktopUiTextActionExecutor executor,
        IDesktopKeyboardTextActionExecutor? keyboardFallback = null)
    {
        _keyboardFallback = keyboardFallback;
        _enabled =
            enabled ??
            throw new ArgumentNullException(
                nameof(enabled));

        _windows =
            windows ??
            throw new ArgumentNullException(
                nameof(windows));

        _ui =
            ui ??
            throw new ArgumentNullException(
                nameof(ui));

        _executor =
            executor ??
            throw new ArgumentNullException(
                nameof(executor));
    }

    public ActionPreparationResult Prepare(
        ActionInvocation invocation) =>
        new(
            false,
            "UI text action memerlukan async preparation.");

    public async Task<ActionPreparationResult>
        PrepareAsync(
            ActionInvocation invocation,
            CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        if (!_enabled())
        {
            return new(
                false,
                "Desktop interaction sedang nonaktif.");
        }

        if (!TryArg(
                invocation,
                "window",
                out string windowQuery) ||
            !TryArg(
                invocation,
                "control",
                out string controlQuery) ||
            !TryArg(
                invocation,
                "value",
                out string value))
        {
            return new(
                false,
                "Target text input tidak lengkap.");
        }

        if (!DesktopUiTextInputPolicy
                .ValidateValue(
                    value,
                    out string valueReason))
        {
            return new(
                false,
                valueReason);
        }

        DesktopWindowResolution resolved =
            _windows.Resolve(
                windowQuery);

        if (resolved.Ambiguous)
        {
            return new(
                false,
                "Window target ambigu.");
        }

        if (!resolved.Found ||
            resolved.Match is null)
        {
            return new(
                false,
                "Window target tidak ditemukan.");
        }

        DesktopWindowTarget window =
            resolved.Match;

        DesktopUiSnapshot snapshot =
            await _ui.CaptureAsync(
                window,
                cancellationToken:
                    cancellationToken);

        if (!snapshot.Success)
        {
            return new(
                false,
                snapshot.Error ??
                "UI Automation gagal membaca window.");
        }

        DesktopUiControlResolution control =
            DesktopUiControlResolver.Resolve(
                snapshot,
                controlQuery,
                "Edit");

        if (control.Ambiguous)
        {
            return new(
                false,
                "Ada beberapa text field yang cocok. Gunakan nama yang lebih spesifik.");
        }

        if (!control.Found ||
            control.Match is null)
        {
            return new(
                false,
                $"Text field \"{controlQuery}\" tidak ditemukan.");
        }

        DesktopUiNodeSnapshot field =
            control.Match;

        if (!DesktopUiTextInputPolicy
                .ValidateTarget(
                    field,
                    out string targetReason))
        {
            return new(
                false,
                targetReason);
        }

        var prepared =
            new PreparedAssistantAction(
                Name,
                new Dictionary<string, string>
                {
                    ["windowId"] =
                        window.Id,

                    ["windowFingerprint"] =
                        window.Fingerprint,

                    ["controlPath"] =
                        field.Path,

                    ["controlFingerprint"] =
                        DesktopUiNodeIdentity
                            .Fingerprint(field),

                    ["value"] =
                        value
                },
                $"Isi text field {field.DisplayName}",
                $"Izinkan Lu-Knight mengganti isi text field {field.DisplayName} pada window {window.DisplayLabel}? " +
                $"Teks sepanjang {value.Length} karakter akan dimasukkan. UI Automation ValuePattern diprioritaskan; " +
                "jika field tidak menyediakan ValuePattern, Lu-Knight boleh menggunakan keyboard fallback tervalidasi pada field yang sama.",
                IncludeInContext: false);

        return new(
            true,
            $"Siap mengisi text field {field.DisplayName}.",
            prepared);
    }

    public async Task<ActionExecutionResult>
        ExecuteAsync(
            PreparedAssistantAction action,
            CancellationToken cancellationToken = default)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        if (!_enabled())
        {
            return new(
                false,
                "Desktop interaction sedang nonaktif.");
        }

        if (!action.Arguments.TryGetValue(
                "windowId",
                out string? windowId) ||
            !action.Arguments.TryGetValue(
                "windowFingerprint",
                out string? windowFingerprint) ||
            !action.Arguments.TryGetValue(
                "controlPath",
                out string? path) ||
            !action.Arguments.TryGetValue(
                "controlFingerprint",
                out string? fingerprint) ||
            !action.Arguments.TryGetValue(
                "value",
                out string? value))
        {
            return new(
                false,
                "UI text target tidak valid.");
        }

        if (!DesktopUiTextInputPolicy
                .ValidateValue(
                    value,
                    out string valueReason))
        {
            return new(
                false,
                valueReason);
        }

        if (!_windows.TryResolveById(
                windowId,
                out DesktopWindowTarget window) ||
            !string.Equals(
                window.Fingerprint,
                windowFingerprint,
                StringComparison.Ordinal))
        {
            return new(
                false,
                "Window berubah sejak konfirmasi.");
        }

        DesktopUiSnapshot snapshot =
            await _ui.CaptureAsync(
                window,
                cancellationToken:
                    cancellationToken);

        if (!snapshot.Success)
        {
            return new(
                false,
                snapshot.Error ??
                "UI Automation gagal membaca ulang window.");
        }

        DesktopUiNodeSnapshot? current =
            snapshot.Nodes
                .FirstOrDefault(
                    x =>
                        x.Path ==
                        path);

        if (current is null ||
            !string.Equals(
                DesktopUiNodeIdentity
                    .Fingerprint(current),
                fingerprint,
                StringComparison.Ordinal))
        {
            return new(
                false,
                "Text field berubah sejak konfirmasi.");
        }

        if (!DesktopUiTextInputPolicy
                .ValidateTarget(
                    current,
                    out string targetReason))
        {
            return new(
                false,
                targetReason);
        }

        DesktopUiTextResult result =
            await _executor.SetTextAsync(
                window,
                path,
                fingerprint,
                value,
                cancellationToken);

        if (result.Success)
        {
            return new(
                true,
                result.Message);
        }

        if (!result.CanKeyboardFallback)
        {
            return new(
                false,
                result.Message);
        }

        if (_keyboardFallback is null)
        {
            return new(
                false,
                "Text field tidak menyediakan ValuePattern dan keyboard fallback tidak tersedia.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        DesktopActionResult keyboard =
            await _keyboardFallback.ReplaceTextAsync(
                window,
                path,
                fingerprint,
                value,
                cancellationToken);

        if (!keyboard.Success)
        {
            return new(
                false,
                $"ValuePattern tidak tersedia. Keyboard fallback juga dibatalkan: {keyboard.Message}");
        }

        return new(
            true,
            $"{keyboard.Message} ValuePattern tidak tersedia, jadi digunakan keyboard fallback tervalidasi.");
    }

    private static bool TryArg(
        ActionInvocation invocation,
        string key,
        out string value)
    {
        if (invocation.Arguments.TryGetValue(
                key,
                out string? found) &&
            !string.IsNullOrEmpty(
                found))
        {
            value =
                found;

            return true;
        }

        value =
            string.Empty;

        return false;
    }
}
