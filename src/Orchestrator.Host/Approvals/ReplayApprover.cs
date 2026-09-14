using System.Globalization;
using Orchestrator.Core.Contracts;
using Orchestrator.Core.Governance;

namespace Orchestrator.Host.Approvals;

/// <summary>
/// Replays the decisions a human took in the recorded interactive session, in order, matched by
/// gate label. The actor is suffixed with the recording date so nobody mistakes a replay for a
/// live approval. Runs out → the run stops, it never invents an approval.
/// </summary>
public sealed class ReplayApprover(IReadOnlyList<RecordedDecision> decisions) : IApprover
{
    private readonly Queue<RecordedDecision> _queue = new(decisions);

    public Task<ApprovalDecision> DecideAsync(ApprovalRequest request, CancellationToken ct)
    {
        if (!_queue.TryPeek(out var next))
        {
            throw new InvalidOperationException($"No recorded decision left for gate '{request.Label}'. Re-record with --live.");
        }

        if (next.Label != request.Label)
        {
            throw new InvalidOperationException($"Recorded decision #{next.Seq} is for '{next.Label}' but the run asked for '{request.Label}'. The workflow changed since it was recorded; re-record with --live.");
        }

        _queue.Dequeue();
        var actor = $"{next.Actor} [recorded {next.DecidedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}]";
        Console.WriteLine($"  ↳ replaying recorded decision #{next.Seq} by {actor}: {next.Kind} — {next.Rationale}");
        return Task.FromResult(new ApprovalDecision(next.Kind, actor, next.Rationale, next.AmbiguityResolutions));
    }
}

/// <summary>Approves everything, choosing recommended options. Only for unattended live smoke runs; the actor name says so.</summary>
public sealed class UnattendedApprover : IApprover
{
    public const string Actor = "unattended-auto-approver";

    public Task<ApprovalDecision> DecideAsync(ApprovalRequest request, CancellationToken ct)
    {
        var resolutions = request.OpenAmbiguities.ToDictionary(a => a.Id, a => a.RecommendedOptionId, StringComparer.Ordinal);
        return Task.FromResult(new ApprovalDecision(DecisionKind.Approved, Actor, "auto-approved (unattended run, recommended options taken)", resolutions));
    }
}
