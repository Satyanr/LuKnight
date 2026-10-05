using System.Collections.ObjectModel;

namespace LuKnight.Assistant;

public sealed record WorkflowStepResult(
    int StepNumber,
    string ActionName,
    AssistantActionRisk Risk,
    DateTimeOffset CompletedAt,
    IReadOnlyDictionary<string, string> Outputs);

public sealed class WorkflowExecutionState
{
    private readonly WorkflowStepResult[] _steps;

    public static WorkflowExecutionState Empty { get; } = new([]);

    private WorkflowExecutionState(IEnumerable<WorkflowStepResult> steps) =>
        _steps = steps.ToArray();

    public IReadOnlyList<WorkflowStepResult> Steps => Array.AsReadOnly(_steps);

    public WorkflowStepResult? Last => _steps.LastOrDefault();

    public WorkflowExecutionState Append(WorkflowStepResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        int expected = _steps.Length + 1;
        if (result.StepNumber != expected)
            throw new InvalidOperationException(
                $"Workflow step result harus berurutan. Expected {expected}, actual {result.StepNumber}.");
        if (expected > LocalMultiStepPlanParser.MaxSteps)
            throw new InvalidOperationException(
                "Workflow state melewati batas jumlah langkah.");

        IReadOnlyDictionary<string, string> frozenOutputs =
            new ReadOnlyDictionary<string, string>(
                new Dictionary<string, string>(
                    result.Outputs,
                    StringComparer.OrdinalIgnoreCase));
        WorkflowStepResult frozen = result with
        {
            Outputs = frozenOutputs
        };
        return new WorkflowExecutionState(_steps.Append(frozen));
    }

    public IReadOnlyDictionary<string, string> BuildRuntimeVariables()
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowStepResult step in _steps)
            foreach ((string key, string value) in step.Outputs)
                variables[$"steps.{step.StepNumber}.{key}"] = value;

        WorkflowStepResult? last = Last;
        if (last is not null)
            foreach ((string key, string value) in last.Outputs)
                variables[$"last.{key}"] = value;

        return new ReadOnlyDictionary<string, string>(variables);
    }
}

public static class WorkflowOutputPolicy
{
    public const int MaxOutputsPerStep = 8;
    public const int MaxValueLength = 500;
    public const int MaxTotalLength = 1200;

    public static IReadOnlyDictionary<string, string> Normalize(
        IReadOnlyDictionary<string, string>? source)
    {
        if (source is null || source.Count == 0)
            return Empty();
        if (source.Count > MaxOutputsPerStep)
            return Empty();

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int total = 0;
        foreach ((string rawKey, string rawValue) in source)
        {
            string key = rawKey?.Trim() ?? string.Empty;
            string value = rawValue?.Trim() ?? string.Empty;
            if (!AssistantSkillPolicy.IsValidParameterName(key) ||
                value.Length is < 1 or > MaxValueLength ||
                value.Any(char.IsControl))
                return Empty();
            total += value.Length;
            if (total > MaxTotalLength)
                return Empty();
            result[key] = value;
        }
        return new ReadOnlyDictionary<string, string>(result);
    }

    private static IReadOnlyDictionary<string, string> Empty() =>
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
