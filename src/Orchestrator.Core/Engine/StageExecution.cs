using Orchestrator.Core.Contracts;

namespace Orchestrator.Core.Engine;

/// <summary>How one stage ended, as seen by the scheduler.</summary>
public abstract record StageExecution(string StageId)
{
    public sealed record Completed(string StageId, IReadOnlyList<Artifact> Artifacts, IReadOnlyList<Decision> Decisions) : StageExecution(StageId);

    /// <summary>Retries, fallback and revisions are exhausted (or were not allowed).</summary>
    public sealed record Failed(string StageId, string Reason, IReadOnlyList<Artifact> Artifacts) : StageExecution(StageId);

    /// <summary>A human said no. The run stops; nothing downstream may proceed.</summary>
    public sealed record Rejected(string StageId, string Rationale) : StageExecution(StageId);

    /// <summary>Cancelled by the safe-stop before finishing.</summary>
    public sealed record Stopped(string StageId) : StageExecution(StageId);
}
