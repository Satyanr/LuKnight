using LuKnight.Services;

namespace LuKnight.Assistant;

internal static class SearchLocationSkillHelper
{
    public static SkillExpansionResult Expand(
        SkillInvocation invocation,
        string location,
        string displayName)
    {
        string query = invocation.Argument.Trim();
        if (!WindowsExplorerActionExecutor.IsValidQuery(query))
            return SkillExpansionResult.Failure(
                $"{displayName} membutuhkan kata pencarian 2–200 karakter tanpa karakter kontrol.");

        AssistantPlanStep[] steps =
        [
            new(0, $"buka {location}"),
            new(1, $"cari {query} di {location}")
        ];
        return SkillExpansionResult.Expanded(
            new AssistantPlan(Array.AsReadOnly(steps)),
            $"Skill {displayName} siap.");
    }
}

public sealed class SearchDownloadsSkill : IAssistantSkill
{
    public string Id => "search-downloads";
    public string DisplayName => "Search Downloads";
    public string Description =>
        "Membuka Downloads lalu mencari file atau folder dengan kata kunci tertentu.";
    public IReadOnlyList<string> Aliases { get; } =
        ["cari-downloads", "cari-unduhan"];
    public SkillExpansionResult Expand(SkillInvocation invocation) =>
        SearchLocationSkillHelper.Expand(invocation, "downloads", DisplayName);
}

public sealed class SearchDocumentsSkill : IAssistantSkill
{
    public string Id => "search-documents";
    public string DisplayName => "Search Documents";
    public string Description =>
        "Membuka Documents lalu mencari file atau folder dengan kata kunci tertentu.";
    public IReadOnlyList<string> Aliases { get; } =
        ["cari-documents", "cari-dokumen"];
    public SkillExpansionResult Expand(SkillInvocation invocation) =>
        SearchLocationSkillHelper.Expand(invocation, "documents", DisplayName);
}

public sealed class SearchDesktopSkill : IAssistantSkill
{
    public string Id => "search-desktop";
    public string DisplayName => "Search Desktop";
    public string Description =>
        "Membuka Desktop lalu mencari file atau folder dengan kata kunci tertentu.";
    public IReadOnlyList<string> Aliases { get; } = ["cari-desktop"];
    public SkillExpansionResult Expand(SkillInvocation invocation) =>
        SearchLocationSkillHelper.Expand(invocation, "desktop", DisplayName);
}

public sealed class SearchPicturesSkill : IAssistantSkill
{
    public string Id => "search-pictures";
    public string DisplayName => "Search Pictures";
    public string Description =>
        "Membuka Pictures lalu mencari file gambar atau folder dengan kata kunci tertentu.";
    public IReadOnlyList<string> Aliases { get; } =
        ["cari-pictures", "cari-gambar"];
    public SkillExpansionResult Expand(SkillInvocation invocation) =>
        SearchLocationSkillHelper.Expand(invocation, "pictures", DisplayName);
}

public static class BuiltInSkillCatalog
{
    public static IReadOnlyList<IAssistantSkill> Create() =>
    [
        new SearchDownloadsSkill(),
        new SearchDocumentsSkill(),
        new SearchDesktopSkill(),
        new SearchPicturesSkill()
    ];
}
