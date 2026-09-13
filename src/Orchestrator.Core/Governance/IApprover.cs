using Orchestrator.Core.Contracts;

namespace Orchestrator.Core.Governance;

/// <summary>
/// The human in the loop. Implementations: an interactive console approver, a recorded approver
/// that replays decisions taken in an earlier interactive session, and test fakes.
/// </summary>
public interface IApprover
{
    Task<ApprovalDecision> DecideAsync(ApprovalRequest request, CancellationToken ct);
}

/// <param name="Label">The gate's label from the workflow, e.g. <c>approve-design</c>.</param>
/// <param name="ArtifactsToReview">What the human is being asked to sign off.</param>
/// <param name="OpenAmbiguities">Questions the agent could not settle; the human may resolve them here.</param>
public sealed record ApprovalRequest(
    string RunId,
    string StageId,
    string Label,
    IReadOnlyList<Artifact> ArtifactsToReview,
    IReadOnlyList<Ambiguity> OpenAmbiguities);

/// <param name="Kind">Approved, Rejected (stop the run) or RevisionRequested (re-run the stage with <paramref name="Rationale"/> as feedback).</param>
/// <param name="AmbiguityResolutions">Ambiguity id → chosen option id.</param>
public sealed record ApprovalDecision(
    DecisionKind Kind,
    string Actor,
    string Rationale,
    IReadOnlyDictionary<string, string> AmbiguityResolutions)
{
    public static ApprovalDecision Approve(string actor, string rationale = "approved") =>
        new(DecisionKind.Approved, actor, rationale, new Dictionary<string, string>(StringComparer.Ordinal));
}
