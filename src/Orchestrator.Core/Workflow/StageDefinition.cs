namespace Orchestrator.Core.Workflow;

/// <summary>One node of the workflow graph.</summary>
/// <param name="Agent">Role name resolved through <c>IAgentRegistry</c>.</param>
/// <param name="Entry">Checked before the agent runs (policies over inputs, optional approval).</param>
/// <param name="Exit">Checked after the agent runs (required artifacts, policies over outputs, optional approval).</param>
/// <param name="Retry">Bounded retry for the stage's own attempts.</param>
/// <param name="FallbackAgent">Role to try once when retries are exhausted; null means fail the stage.</param>
/// <param name="OnFailure">What to do if the stage still fails: stop the run, or loop back to an earlier stage.</param>
public sealed record StageDefinition(
    string Id,
    string Agent,
    IReadOnlyList<string> DependsOn,
    GateDefinition Entry,
    GateDefinition Exit,
    RetryDefinition Retry,
    string? FallbackAgent,
    FailureHandling OnFailure)
{
    public bool IsHighImpact => Entry.Approval is not null || Exit.Approval is not null;
}

/// <param name="RerunFrom">Stage to invalidate and re-run with this stage's failure as feedback.</param>
/// <param name="MaxLoops">How many times the loop may happen before the run is stopped.</param>
public sealed record FailureHandling(string? RerunFrom, int MaxLoops)
{
    public static readonly FailureHandling StopRun = new(null, 0);
}

public sealed record RetryDefinition(int MaxAttempts, TimeSpan BaseDelay)
{
    public static readonly RetryDefinition None = new(1, TimeSpan.Zero);
}
