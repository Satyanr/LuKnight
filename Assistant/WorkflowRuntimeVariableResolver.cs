using System.Text.RegularExpressions;

namespace LuKnight.Assistant;

public sealed record WorkflowRuntimeResolution(
    bool Success, string? Command = null, string? Error = null);

public static class WorkflowRuntimeVariableResolver
{
    private static readonly Regex PlaceholderPattern = new(
        @"\{(last\.(?:window|process))\}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool Contains(string command) =>
        !string.IsNullOrWhiteSpace(command) && PlaceholderPattern.IsMatch(command);

    public static WorkflowRuntimeResolution Resolve(
        string command, IReadOnlyDictionary<string, string> variables)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(variables);
        string? missing = null;
        string resolved = PlaceholderPattern.Replace(command, match =>
        {
            string name = match.Groups[1].Value;
            if (variables.TryGetValue(name, out string? value)) return value;
            missing ??= name;
            return match.Value;
        });
        if (missing is not null)
            return new(false, Error: $"Variable workflow '{{{missing}}}' belum tersedia.");
        if (resolved.Length == 0 || resolved.Length > LocalMultiStepPlanParser.MaxStepLength ||
            resolved.Any(char.IsControl))
            return new(false, Error: "Perintah hasil variable workflow tidak valid.");
        return new(true, resolved);
    }
}
