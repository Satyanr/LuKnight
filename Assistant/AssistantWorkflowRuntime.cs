namespace LuKnight.Assistant;

public enum WorkflowRuntimeStatus
{
    Ready,
    Completed,
    Expired,
    Invalid
}

public sealed record WorkflowRuntimeSession(
    Guid Id,
    AssistantPlan Plan,
    int CurrentStepIndex,
    DateTimeOffset ExpiresAt,
    WorkflowExecutionState WorkflowState)
{
    public bool IsCompleted => CurrentStepIndex >= Plan.Count;
}

public sealed record WorkflowRuntimeCommandResult(
    WorkflowRuntimeStatus Status,
    WorkflowRuntimeSession Session,
    AssistantPlanStep? Step = null,
    string? Command = null,
    string? Error = null)
{
    public bool Success => Status == WorkflowRuntimeStatus.Ready;
}

public sealed record WorkflowRuntimeAdvanceResult(
    WorkflowRuntimeStatus Status,
    WorkflowRuntimeSession Session,
    string? Error = null)
{
    public bool Success => Status is
        WorkflowRuntimeStatus.Ready or WorkflowRuntimeStatus.Completed;

    public bool Completed => Status == WorkflowRuntimeStatus.Completed;
}

public sealed class AssistantWorkflowRuntime
{
    public WorkflowRuntimeSession Start(
        AssistantPlan plan,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime));

        AssistantPlan frozen = FreezePlan(plan);
        return new WorkflowRuntimeSession(
            Guid.NewGuid(),
            frozen,
            0,
            now.Add(lifetime),
            WorkflowExecutionState.Empty);
    }

    public bool IsExpired(
        WorkflowRuntimeSession session,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        return now >= session.ExpiresAt;
    }

    public WorkflowRuntimeCommandResult ResolveCurrent(
        WorkflowRuntimeSession session,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (IsExpired(session, now))
            return new(
                WorkflowRuntimeStatus.Expired,
                session,
                Error: "Workflow sudah kedaluwarsa.");
        if (session.IsCompleted)
            return new(WorkflowRuntimeStatus.Completed, session);

        AssistantPlanStep step = session.Plan.Steps[session.CurrentStepIndex];
        string command = step.Command;
        if (session.Plan.AllowRuntimeVariables)
        {
            WorkflowRuntimeResolution resolution =
                WorkflowRuntimeVariableResolver.Resolve(
                    command,
                    session.WorkflowState.BuildRuntimeVariables());
            if (!resolution.Success || resolution.Command is null)
                return new(
                    WorkflowRuntimeStatus.Invalid,
                    session,
                    Step: step,
                    Error: resolution.Error ??
                        "Variable workflow tidak dapat diselesaikan.");
            command = resolution.Command;
        }

        return new(
            WorkflowRuntimeStatus.Ready,
            session,
            Step: step,
            Command: command);
    }

    public WorkflowRuntimeAdvanceResult Advance(
        WorkflowRuntimeSession session,
        WorkflowStepResult stepResult,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(stepResult);
        if (IsExpired(session, now))
            return new(
                WorkflowRuntimeStatus.Expired,
                session,
                "Workflow sudah kedaluwarsa.");
        if (session.IsCompleted)
            return new(
                WorkflowRuntimeStatus.Invalid,
                session,
                "Workflow sudah selesai.");

        int expectedStep = session.CurrentStepIndex + 1;
        if (stepResult.StepNumber != expectedStep)
            return new(
                WorkflowRuntimeStatus.Invalid,
                session,
                $"Workflow result tidak sesuai step aktif. Expected {expectedStep}, actual {stepResult.StepNumber}.");

        WorkflowExecutionState nextState;
        try
        {
            nextState = session.WorkflowState.Append(stepResult);
        }
        catch (InvalidOperationException ex)
        {
            return new(
                WorkflowRuntimeStatus.Invalid,
                session,
                ex.Message);
        }

        WorkflowRuntimeSession next = session with
        {
            CurrentStepIndex = session.CurrentStepIndex + 1,
            WorkflowState = nextState
        };
        return new(
            next.IsCompleted
                ? WorkflowRuntimeStatus.Completed
                : WorkflowRuntimeStatus.Ready,
            next);
    }

    private static AssistantPlan FreezePlan(AssistantPlan plan)
    {
        if (plan.Count is < 1 or > LocalMultiStepPlanParser.MaxSteps)
            throw new ArgumentException(
                "Jumlah langkah workflow tidak valid.",
                nameof(plan));

        int total = 0;
        AssistantPlanStep[] steps = plan.Steps
            .Select((step, index) =>
            {
                string command = step.Command?.Trim() ?? string.Empty;
                if (command.Length is < 1 or > LocalMultiStepPlanParser.MaxStepLength)
                    throw new ArgumentException(
                        $"Workflow step {index + 1} tidak valid.",
                        nameof(plan));
                if (command.Any(char.IsControl))
                    throw new ArgumentException(
                        $"Workflow step {index + 1} mengandung karakter kontrol.",
                        nameof(plan));
                total += command.Length;
                if (total > LocalMultiStepPlanParser.MaxInputLength)
                    throw new ArgumentException(
                        "Total workflow terlalu panjang.",
                        nameof(plan));
                return new AssistantPlanStep(index, command);
            })
            .ToArray();

        return new AssistantPlan(
            Array.AsReadOnly(steps),
            plan.AllowRuntimeVariables);
    }
}
