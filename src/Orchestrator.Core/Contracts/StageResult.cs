namespace Orchestrator.Core.Contracts;

/// <summary>What an agent hands back to the executor for one stage attempt.</summary>
public sealed record StageResult(StageOutcome Outcome, IReadOnlyList<Artifact> Artifacts, string? FailureReason = null)
{
    public static StageResult Success(params Artifact[] artifacts) => new(StageOutcome.Succeeded, artifacts);

    public static StageResult Failure(string reason, params Artifact[] artifacts) => new(StageOutcome.Failed, artifacts, reason);
}

public enum StageOutcome
{
    Succeeded,
    Failed,
}
