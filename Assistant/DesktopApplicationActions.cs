using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class OpenDesktopApplicationAction : IAssistantAction
{
    private readonly Func<bool> _enabled;
    private readonly IDesktopActionExecutor _executor;

    public string Name => BuiltInActionNames.DesktopOpenApplication;

    public OpenDesktopApplicationAction(Func<bool> enabled, IDesktopActionExecutor executor)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public ActionPreparationResult Prepare(ActionInvocation invocation)
    {
        if (!_enabled())
            return new ActionPreparationResult(false, "Desktop actions sedang nonaktif. Aktifkan melalui Settings → AI & Chat.");

        if (!TryResolve(invocation, out DesktopAppTarget app))
            return new ActionPreparationResult(false, "Aplikasi itu belum diizinkan.");

        var prepared = new PreparedAssistantAction(
            Name,
            new Dictionary<string, string> { ["appId"] = app.Id },
            $"Buka {app.DisplayName}",
            $"Izinkan Lu-Knight membuka {app.DisplayName}?");

        return new ActionPreparationResult(true, $"Siap membuka {app.DisplayName}.", prepared);
    }

    public Task<ActionExecutionResult> ExecuteAsync(PreparedAssistantAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_enabled())
            return Task.FromResult(new ActionExecutionResult(false, "Desktop actions sedang nonaktif."));

        if (!TryResolve(action, out DesktopAppTarget app))
            return Task.FromResult(new ActionExecutionResult(false, "Target aplikasi tidak valid."));

        DesktopActionResult result = _executor.Open(app);
        return Task.FromResult(new ActionExecutionResult(result.Success, result.Message));
    }

    private static bool TryResolve(ActionInvocation invocation, out DesktopAppTarget app)
    {
        if (!invocation.Arguments.TryGetValue("appId", out string? id))
        {
            app = default!;
            return false;
        }

        return DesktopAppCatalog.TryResolveById(id, out app);
    }

    private static bool TryResolve(PreparedAssistantAction action, out DesktopAppTarget app)
    {
        if (!action.Arguments.TryGetValue("appId", out string? id))
        {
            app = default!;
            return false;
        }

        return DesktopAppCatalog.TryResolveById(id, out app);
    }
}

public sealed class FocusDesktopApplicationAction : IAssistantAction
{
    private readonly Func<bool> _enabled;
    private readonly IDesktopActionExecutor _executor;

    public string Name => BuiltInActionNames.DesktopFocusApplication;

    public FocusDesktopApplicationAction(Func<bool> enabled, IDesktopActionExecutor executor)
    {
        _enabled = enabled ?? throw new ArgumentNullException(nameof(enabled));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
    }

    public ActionPreparationResult Prepare(ActionInvocation invocation)
    {
        if (!_enabled())
            return new ActionPreparationResult(false, "Desktop actions sedang nonaktif. Aktifkan melalui Settings → AI & Chat.");

        if (!TryResolve(invocation, out DesktopAppTarget app))
            return new ActionPreparationResult(false, "Aplikasi itu belum diizinkan.");

        var prepared = new PreparedAssistantAction(
            Name,
            new Dictionary<string, string> { ["appId"] = app.Id },
            $"Fokus {app.DisplayName}",
            $"Izinkan Lu-Knight memfokuskan window {app.DisplayName}?");

        return new ActionPreparationResult(true, $"Siap memfokuskan {app.DisplayName}.", prepared);
    }

    public Task<ActionExecutionResult> ExecuteAsync(PreparedAssistantAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_enabled())
            return Task.FromResult(new ActionExecutionResult(false, "Desktop actions sedang nonaktif."));

        if (!TryResolve(action, out DesktopAppTarget app))
            return Task.FromResult(new ActionExecutionResult(false, "Target aplikasi tidak valid."));

        DesktopActionResult result = _executor.Focus(app);
        return Task.FromResult(new ActionExecutionResult(result.Success, result.Message));
    }

    private static bool TryResolve(ActionInvocation invocation, out DesktopAppTarget app)
    {
        if (!invocation.Arguments.TryGetValue("appId", out string? id))
        {
            app = default!;
            return false;
        }

        return DesktopAppCatalog.TryResolveById(id, out app);
    }

    private static bool TryResolve(PreparedAssistantAction action, out DesktopAppTarget app)
    {
        if (!action.Arguments.TryGetValue("appId", out string? id))
        {
            app = default!;
            return false;
        }

        return DesktopAppCatalog.TryResolveById(id, out app);
    }
}
