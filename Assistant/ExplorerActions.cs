using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class OpenExplorerFolderAction(Func<bool> enabled, IExplorerActionExecutor executor) : IAssistantAction
{
    public string Name => BuiltInActionNames.DesktopOpenFolder;

    public ActionPreparationResult Prepare(ActionInvocation invocation)
    {
        if (!enabled()) return new(false, "Desktop actions sedang nonaktif.");
        if (!TryLocation(invocation.Arguments, out var location)) return new(false, "Lokasi Explorer tidak valid.");
        return new(true, $"Siap membuka {location.DisplayName}.", new(Name,
            new Dictionary<string, string> { ["locationId"] = location.Id, ["path"] = location.Path },
            $"Buka {location.DisplayName}", $"Izinkan Lu-Knight membuka File Explorer di {location.DisplayName}?"));
    }

    public Task<ActionExecutionResult> ExecuteAsync(PreparedAssistantAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!enabled()) return Task.FromResult(new ActionExecutionResult(false, "Desktop actions sedang nonaktif."));
        if (!TryLocation(action.Arguments, out var location) || !action.Arguments.TryGetValue("path", out var path) || path != location.Path)
            return Task.FromResult(new ActionExecutionResult(false, "Lokasi Explorer berubah atau tidak valid. Ulangi perintah."));
        var result = executor.OpenFolder(location.Path);
        return Task.FromResult(new ActionExecutionResult(result.Success, result.Message));
    }

    private static bool TryLocation(IReadOnlyDictionary<string, string> args, out ExplorerLocationTarget location)
    {
        location = default!;
        return args.TryGetValue("locationId", out var id) && ExplorerLocationCatalog.TryResolveById(id, out location);
    }
}

public sealed class SearchExplorerAction(Func<bool> enabled, IExplorerActionExecutor executor) : IAssistantAction
{
    public string Name => BuiltInActionNames.DesktopSearchExplorer;

    public ActionPreparationResult Prepare(ActionInvocation invocation)
    {
        if (!enabled()) return new(false, "Desktop actions sedang nonaktif.");
        if (!TryArguments(invocation.Arguments, out string query, out var location)) return new(false, "Query atau lokasi pencarian tidak valid.");
        var args = new Dictionary<string, string> { ["query"] = query };
        if (location is not null) { args["locationId"] = location.Id; args["path"] = location.Path; }
        string scope = location?.DisplayName ?? "Windows Search";
        return new(true, "Siap membuka pencarian Explorer.", new(Name, args, "Cari di Explorer",
            $"Izinkan Lu-Knight mencari \"{query}\" di {scope}?"));
    }

    public Task<ActionExecutionResult> ExecuteAsync(PreparedAssistantAction action, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!enabled()) return Task.FromResult(new ActionExecutionResult(false, "Desktop actions sedang nonaktif."));
        if (!TryArguments(action.Arguments, out string query, out var location) ||
            (location is not null && (!action.Arguments.TryGetValue("path", out var path) || path != location.Path)))
            return Task.FromResult(new ActionExecutionResult(false, "Query atau lokasi pencarian berubah atau tidak valid. Ulangi perintah."));
        var result = executor.Search(query, location?.Path);
        return Task.FromResult(new ActionExecutionResult(result.Success, result.Message));
    }

    private static bool TryArguments(IReadOnlyDictionary<string, string> args, out string query, out ExplorerLocationTarget? location)
    {
        query = ""; location = null;
        if (!args.TryGetValue("query", out var value) || !WindowsExplorerActionExecutor.IsValidQuery(value)) return false;
        query = value.Trim();
        if (args.TryGetValue("locationId", out var id))
        {
            if (!ExplorerLocationCatalog.TryResolveById(id, out var resolved)) return false;
            location = resolved;
        }
        return true;
    }
}
