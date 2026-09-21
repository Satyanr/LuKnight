using LuKnight.Services;

namespace LuKnight.Assistant;

public sealed class SearchDownloadsSkill : IAssistantSkill
{
    public string Id => "search-downloads";
    public string DisplayName => "Search Downloads";
    public IReadOnlyList<string> Aliases { get; } = ["cari-downloads"];

    public SkillExpansionResult Expand(SkillInvocation invocation)
    {
        string query = invocation.Argument.Trim();
        if (!WindowsExplorerActionExecutor.IsValidQuery(query))
            return SkillExpansionResult.Failure(
                "Search Downloads membutuhkan kata pencarian 2–200 karakter tanpa karakter kontrol.");

        AssistantPlanStep[] steps =
        [
            new(0, "buka downloads"),
            new(1, $"cari {query} di downloads")
        ];
        return SkillExpansionResult.Expanded(
            new AssistantPlan(Array.AsReadOnly(steps)),
            "Skill Search Downloads siap.");
    }
}
