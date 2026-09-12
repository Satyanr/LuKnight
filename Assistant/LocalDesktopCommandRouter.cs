using System.Text.RegularExpressions;
using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class LocalDesktopCommandRouter
{
    private readonly IDesktopAppCatalog _apps;
    public LocalDesktopCommandRouter(IDesktopAppCatalog apps) => _apps = apps ?? throw new ArgumentNullException(nameof(apps));
    public IDesktopAppCatalog Applications => _apps;

    private static readonly string[] OpenPrefixes = ["bukakan aplikasi", "bukain aplikasi", "buka aplikasi", "open app",
        "bukakan", "bukain", "buka", "jalankan", "jalanin", "launch", "open", "start"];
    private static readonly string[] FocusPrefixes = ["fokuskan ke", "fokus ke", "fokuskan", "fokus", "pindah ke", "balik ke", "switch to", "focus app", "focus"];

    public AssistantIntent? TryRoute(string input)
    {
        string text = NormalizeCommand(input);
        if (text.Length == 0) return null;
        if (TrySearch(text) is { } search) return search;
        if (Extract(text, FocusPrefixes) is { } focus) return Application(focus, true);
        if (Extract(text, OpenPrefixes) is not { } target) return null;

        if (target.Equals("explorer", StringComparison.OrdinalIgnoreCase) || target.Equals("file explorer", StringComparison.OrdinalIgnoreCase))
            return Folder("home");
        if (Extract(target, ["file explorer di", "explorer di", "folder", "explorer"]) is { } location)
            return Folder(location);
        if (ExplorerLocationCatalog.TryResolve(target, out var known))
            return LocationAction(known.Id);
        return Application(target, false);
    }

    private AssistantIntent Application(string target, bool focus)
    {
        if (string.IsNullOrWhiteSpace(target)) return AssistantIntent.RespondLocal("Sebutkan nama aplikasi yang ingin dibuka atau difokuskan.");
        if (target.Length > 200 || DesktopAppPolicy.IsRestrictedQuery(target))
            return AssistantIntent.RespondLocal("Perintah ini tidak diizinkan. Gunakan nama aplikasi biasa yang terdaftar di Windows; shell, script, dan executable path tidak didukung.");
        var result = _apps.Resolve(target);
        if (result.Found && result.Match is { } app)
            return AssistantIntent.UseAction(new(focus ? BuiltInActionNames.DesktopFocusApplication : BuiltInActionNames.DesktopOpenApplication,
                new Dictionary<string, string> { ["appId"] = app.Id }));
        if (result.Ambiguous)
            return AssistantIntent.RespondLocal($"Aku menemukan beberapa aplikasi yang mirip: {string.Join(", ", result.Alternatives.Take(3).Select(a => a.DisplayName))}. Sebutkan nama yang lebih spesifik.");
        return AssistantIntent.RespondLocal($"Aku tidak menemukan aplikasi \"{target}\" di aplikasi Windows yang terdaftar.");
    }

    private static AssistantIntent Folder(string target) => ExplorerLocationCatalog.TryResolve(target, out var location)
        ? LocationAction(location.Id)
        : AssistantIntent.RespondLocal("Lokasi folder belum didukung. Pilih Home, Desktop, Documents, Downloads, Pictures, Music, atau Videos.");

    private static AssistantIntent LocationAction(string id) => AssistantIntent.UseAction(new(BuiltInActionNames.DesktopOpenFolder,
        new Dictionary<string, string> { ["locationId"] = id }));

    private static AssistantIntent? TrySearch(string text)
    {
        string? query = Extract(text, ["buka file explorer dan cari", "buka explorer dan cari", "open explorer and search",
            "carikan file", "carikan folder", "carikan", "cari file", "cari folder", "cari", "search files", "find file"]);
        if (query is null) return null;
        string? locationId = null;
        foreach (var item in ExplorerLocationCatalog.Locations.SelectMany(l => l.Aliases.Select(a => (Location: l, Alias: a)))
                     .OrderByDescending(x => x.Alias.Length))
        {
            string suffix = " di " + item.Alias;
            if (!query.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            query = query[..^suffix.Length].Trim();
            locationId = item.Location.Id;
            break;
        }
        // Do not silently search everywhere when an explicit scope is unrecognized.
        if (locationId is null && Regex.IsMatch(query, @"\sdi\s", RegexOptions.IgnoreCase))
            return AssistantIntent.RespondLocal("Lokasi pencarian belum dikenal. Gunakan nama folder seperti Documents atau Downloads.");
        if (!WindowsExplorerActionExecutor.IsValidQuery(query))
            return AssistantIntent.RespondLocal("Kata pencarian harus berisi 2 sampai 200 karakter tanpa karakter kontrol.");
        var arguments = new Dictionary<string, string> { ["query"] = query };
        if (locationId is not null) arguments["locationId"] = locationId;
        return AssistantIntent.UseAction(new(BuiltInActionNames.DesktopSearchExplorer, arguments));
    }

    private static string? Extract(string text, IEnumerable<string> prefixes)
    {
        foreach (string prefix in prefixes.OrderByDescending(p => p.Length))
        {
            if (text.Equals(prefix, StringComparison.OrdinalIgnoreCase)) return "";
            if (text.StartsWith(prefix + " ", StringComparison.OrdinalIgnoreCase)) return text[(prefix.Length + 1)..].Trim();
        }
        return null;
    }

    private static string NormalizeCommand(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        string text = Regex.Replace(input.Trim(), @"[ \t]+", " ").TrimEnd('?', '!', '.', ',').TrimEnd();
        string[] leading = ["bantu saya", "tolong", "please", "coba", "eh", "hey", "bisa", "bisakah", "boleh", "bantu"];
        while (Extract(text, leading) is { } rest && rest != text) text = rest;
        bool changed;
        do
        {
            changed = false;
            foreach (string suffix in new[] { " dong", " ya", " yah", " deh", " please" })
                if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                { text = text[..^suffix.Length].TrimEnd(); changed = true; break; }
        } while (changed);
        return text;
    }
}
