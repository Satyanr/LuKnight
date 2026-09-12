using System.Security.Cryptography;
using System.Text;

namespace LuKnight.Services;

public enum DesktopAppSource { BuiltIn, StartMenu, AppPaths, AppsFolder }

public sealed record DesktopAppTarget(
    string Id, string DisplayName, string LaunchTarget,
    IReadOnlyList<string> ProcessNames, IReadOnlyList<string> Aliases, DesktopAppSource Source)
{
    public string? ResolvedExecutable { get; init; }
    public string Arguments { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string? RegistrationIdentity { get; init; }
    public string? AppUserModelId { get; init; }
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{LaunchTarget}|{ResolvedExecutable}|{Arguments}|{WorkingDirectory}|{AppUserModelId}")));
}

public sealed record DesktopAppResolution(
    DesktopAppTarget? Match, IReadOnlyList<DesktopAppTarget> Alternatives, bool Ambiguous)
{
    public bool Found => Match is not null && !Ambiguous;
}

public interface IDesktopAppCatalog
{
    IReadOnlyList<DesktopAppTarget> Applications { get; }
    DesktopAppResolution Resolve(string query);
    bool TryResolveById(string id, out DesktopAppTarget target);
    void Refresh();
}

public sealed class DesktopAppCatalogService : IDesktopAppCatalog
{
    public static IDesktopAppCatalog Shared { get; } = new DesktopAppCatalogService();
    private readonly object _sync = new();
    private readonly Func<IEnumerable<DesktopAppTarget>> _discover;
    private readonly Func<DateTimeOffset> _clock;
    private IReadOnlyList<DesktopAppTarget> _applications = Array.Empty<DesktopAppTarget>();
    private DateTimeOffset _lastRefresh = DateTimeOffset.MinValue;

    public DesktopAppCatalogService(Func<IEnumerable<DesktopAppTarget>>? discover = null,
        Func<DateTimeOffset>? clock = null)
    {
        _discover = discover ?? WindowsDesktopAppDiscovery.Discover;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<DesktopAppTarget> Applications
    {
        get
        {
            lock (_sync)
            {
                if (_clock() - _lastRefresh >= TimeSpan.FromMinutes(10)) Refresh();
                return _applications;
            }
        }
    }

    public void Refresh()
    {
        lock (_sync)
        {
            var discovered = _discover().Where(DesktopAppPolicy.IsAllowed).ToList();
            // Merge duplicate registrations of the same executable/arguments, not different versions.
            var apps = discovered.Where(a => a.Source is not (DesktopAppSource.BuiltIn or DesktopAppSource.AppsFolder))
                .GroupBy(a => a.RegistrationIdentity ?? $"{a.ResolvedExecutable ?? a.LaunchTarget}|{a.Arguments}", StringComparer.OrdinalIgnoreCase)
                .Select(group => Merge(group.OrderByDescending(a => a.RegistrationIdentity is not null && a.Arguments.Length > 0)
                    .ThenBy(a => a.Source).ToArray())).ToList();
            foreach (var builtin in discovered.Where(a => a.Source == DesktopAppSource.BuiltIn))
            {
                var matches = apps.Where(a => a.ProcessNames.Intersect(builtin.ProcessNames, StringComparer.OrdinalIgnoreCase).Any()).ToArray();
                if (matches.Length == 0) apps.Add(builtin);
                else foreach (var match in matches)
                    apps[apps.IndexOf(match)] = match with
                    {
                        Id = matches.Length == 1 ? builtin.Id : match.Id,
                        Aliases = Array.AsReadOnly(match.Aliases.Concat(builtin.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
                    };
            }
            foreach (var packaged in discovered.Where(a => a.Source == DesktopAppSource.AppsFolder))
            {
                var existing = apps.FirstOrDefault(a =>
                    DesktopNameNormalizer.Normalize(a.DisplayName) == DesktopNameNormalizer.Normalize(packaged.DisplayName));
                if (existing is null) apps.Add(packaged);
                else apps[apps.IndexOf(existing)] = existing with
                {
                    Aliases = Array.AsReadOnly(existing.Aliases.Concat(packaged.Aliases)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
                };
            }
            _applications = Array.AsReadOnly(apps.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First()).OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray());
            _lastRefresh = _clock();
        }
    }

    private static DesktopAppTarget Merge(DesktopAppTarget[] group) => group[0] with
    {
        Aliases = Array.AsReadOnly(group.SelectMany(a => a.Aliases).Distinct(StringComparer.OrdinalIgnoreCase).ToArray()),
        ProcessNames = Array.AsReadOnly(group.SelectMany(a => a.ProcessNames).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
    };

    public DesktopAppResolution Resolve(string query)
    {
        string normalized = DesktopNameNormalizer.Normalize(query);
        if (normalized.Length is < 2 or > 200 || DesktopAppPolicy.IsRestrictedQuery(query))
            return new(null, [], false);
        var exactNames = Applications.Where(a => DesktopNameNormalizer.Normalize(a.DisplayName) == normalized).ToArray();
        if (exactNames.Length == 1) return new(exactNames[0], exactNames, false);
        var ranked = Rank(normalized);
        // Rate-limit misses: repeated unknown commands must not repeatedly scan disk/registry.
        if (ranked.Length == 0)
        {
            lock (_sync)
                if (_clock() - _lastRefresh >= TimeSpan.FromSeconds(30)) Refresh();
            ranked = Rank(normalized);
        }
        if (ranked.Length == 0 || ranked[0].Score < .72)
            return new(null, ranked.Select(x => x.App).ToArray(), false);
        bool ambiguous = ranked.Length > 1 && ranked[1].Score >= .72 && ranked[0].Score - ranked[1].Score < .08;
        var alternatives = ambiguous ? ranked.Where(x => x.Score >= .72 && ranked[0].Score - x.Score < .08) : ranked.AsEnumerable();
        return new(ambiguous ? null : ranked[0].App, alternatives.Select(x => x.App).ToArray(), ambiguous);
    }

    private (DesktopAppTarget App, double Score)[] Rank(string query) => Applications
        .Select(a => (App: a, Score: a.Aliases.Select(alias => Score(query, DesktopNameNormalizer.Normalize(alias))).DefaultIfEmpty().Max()))
        .Where(x => x.Score >= .55).OrderByDescending(x => x.Score)
        .ThenBy(x => x.App.DisplayName, StringComparer.OrdinalIgnoreCase).Take(4).ToArray();

    private static double Score(string query, string candidate)
    {
        if (query == candidate) return 1;
        if (query.Length < 3) return 0;
        double score = candidate.StartsWith(query + " ", StringComparison.Ordinal) || candidate.EndsWith(" " + query, StringComparison.Ordinal)
            ? .94 : candidate.Contains(query, StringComparison.Ordinal) ? .90 : 0;
        return Math.Max(score, DesktopNameNormalizer.Similarity(query, candidate));
    }

    public bool TryResolveById(string id, out DesktopAppTarget target)
    {
        target = Applications.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))!;
        return target is not null && DesktopAppPolicy.IsAllowed(target);
    }

    public static string CreateId(string value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(value.ToLowerInvariant())))[..16].ToLowerInvariant();
}
