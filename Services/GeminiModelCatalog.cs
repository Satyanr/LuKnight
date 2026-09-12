namespace LuKnight.Services;

public static class GeminiModelCatalog
{
    public static IReadOnlyList<string> AssistantModels { get; } = Array.AsReadOnly(new[]
    {
        "gemini-3.8-flash", "gemini-3.7-flash", "gemini-3.6-flash",
        "gemini-3.5-flash", "gemini-3.5-flash-lite"
    });

    public static IReadOnlyList<string> BuildFailoverSequence(string primary, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(primary);
        string selected = primary.Trim();
        if (!enabled) return Array.AsReadOnly(new[] { selected });
        int index = AssistantModels.ToList().FindIndex(m => m.Equals(selected, StringComparison.OrdinalIgnoreCase));
        return Array.AsReadOnly(new[] { selected }.Concat(AssistantModels.Skip(index + 1))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }
}
