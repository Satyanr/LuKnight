using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class FocusDesktopWindowAction : IAssistantAction
{
    private readonly Func<bool> _enabled;
    private readonly IDesktopWindowTargetCatalog _windows;
    private readonly IDesktopWindowActionExecutor _executor;

    public string Name => BuiltInActionNames.DesktopFocusWindow;

    public FocusDesktopWindowAction(
        Func<bool> enabled,
        IDesktopWindowTargetCatalog windows,
        IDesktopWindowActionExecutor executor)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public ActionPreparationResult Prepare(ActionInvocation invocation)
    {
        if (!_enabled())
            return new(false, "Desktop actions sedang nonaktif.");

        if (!invocation.Arguments.TryGetValue("windowId", out string? id) ||
            !_windows.TryResolveById(id, out DesktopWindowTarget target))
        {
            return new(false, "Window target tidak lagi tersedia.");
        }

        var prepared = new PreparedAssistantAction(
            Name,
            new Dictionary<string, string>
            {
                ["windowId"] = target.Id,
                ["fingerprint"] = target.Fingerprint
            },
            $"Fokus {target.DisplayLabel}",
            $"Izinkan Lu-Knight memfokuskan window {target.DisplayLabel}?",
            IncludeInContext: false);

        return new(true, $"Siap memfokuskan {target.DisplayLabel}.", prepared);
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        PreparedAssistantAction action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_enabled())
            return Task.FromResult(new ActionExecutionResult(false, "Desktop actions sedang nonaktif."));

        if (!action.Arguments.TryGetValue("windowId", out string? id) ||
            !action.Arguments.TryGetValue("fingerprint", out string? fingerprint) ||
            !_windows.TryResolveById(id, out DesktopWindowTarget current) ||
            !string.Equals(current.Fingerprint, fingerprint, StringComparison.Ordinal))
        {
            return Task.FromResult(new ActionExecutionResult(
                false,
                "Window target berubah atau sudah ditutup. Ulangi perintah."));
        }

        DesktopActionResult result = _executor.Focus(current);
        return Task.FromResult(new ActionExecutionResult(result.Success, result.Message));
    }
}
