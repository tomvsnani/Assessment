namespace Orchestrator.Core.State;

/// <summary>
/// One immutable fact about a run. The event log is the source of truth; status, lineage,
/// audit and metrics are all projections over it (see <c>State/Projections</c>).
/// </summary>
public sealed record RunEvent(
    long Seq,
    DateTimeOffset At,
    string RunId,
    EventKind Kind,
    string? StageId,
    IReadOnlyDictionary<string, string> Data)
{
    public string this[string key] => Data.TryGetValue(key, out var value) ? value : string.Empty;
}

public enum EventKind
{
    RunStarted,
    StageScheduled,
    StageStarted,
    StageAttemptFailed,
    StageRetryScheduled,
    StageFallbackUsed,
    StageCompleted,
    StageFailed,
    StageInvalidated,
    ArtifactProduced,
    PolicyEvaluated,
    ApprovalRequested,
    ApprovalDecided,
    ReplanTriggered,
    CompensationRun,
    RollbackCompleted,
    SafeStopTriggered,
    RunCompleted,
    RunFailed,
}
