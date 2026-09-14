namespace Orchestrator.Core.Contracts;

/// <summary>What an agent hands back to the executor for one stage attempt.</summary>
/// <param name="IsVerdict">
/// True when the failure is the agent's considered judgement (tests failed, reviewer requested
/// changes) and may therefore trigger the workflow's <c>on_failure</c> re-plan. False for a
/// malformed or missing output, which is only ever retried within the stage.
/// </param>
public sealed record StageResult(StageOutcome Outcome, IReadOnlyList<Artifact> Artifacts, string? FailureReason = null, bool IsVerdict = true)
{
    public static StageResult Success(params Artifact[] artifacts) => new(StageOutcome.Succeeded, artifacts);

    public static StageResult Failure(string reason, params Artifact[] artifacts) => new(StageOutcome.Failed, artifacts, reason);

    /// <summary>The agent produced something unusable; not a judgement about the work.</summary>
    public static StageResult Malformed(string reason, params Artifact[] artifacts) => new(StageOutcome.Failed, artifacts, reason, IsVerdict: false);
}

public enum StageOutcome
{
    Succeeded,
    Failed,
}
