using System.Globalization;
using Orchestrator.Core.Contracts;
using Orchestrator.Core.State;

namespace Orchestrator.Core.Governance;

/// <summary>
/// Turns a gate's approval label into a recorded human decision. Records the request, the
/// decision and every ambiguity resolution as events, and returns the typed
/// <see cref="Decision"/>s so later stages can cite them.
/// </summary>
public sealed class ApprovalGate(IApprover approver, EventStore events)
{
    private int _decisionCounter;

    public async Task<ApprovalOutcome> RequestAsync(
        string stageId,
        string label,
        IReadOnlyList<Artifact> artifactsToReview,
        IReadOnlyList<Ambiguity> openAmbiguities,
        CancellationToken ct)
    {
        events.Append(EventKind.ApprovalRequested, stageId,
            ("label", label),
            ("artifacts", string.Join(",", artifactsToReview.Select(a => $"{a.Name}@{a.ContentHash}"))),
            ("openAmbiguities", openAmbiguities.Count.ToString(CultureInfo.InvariantCulture)));

        var request = new ApprovalRequest(events.RunId, stageId, label, artifactsToReview, openAmbiguities);
        var decision = await approver.DecideAsync(request, ct);

        var decisions = new List<Decision>();
        var cited = artifactsToReview.Select(a => a.Name).ToList();
        var primary = NewDecision(stageId, decision.Actor, decision.Kind, decision.Rationale, cited);
        decisions.Add(primary);
        events.Append(EventKind.ApprovalDecided, stageId,
            ("decisionId", primary.Id), ("label", label), ("actor", decision.Actor),
            ("decision", decision.Kind.ToString()), ("rationale", decision.Rationale), ("cites", string.Join(",", cited)));

        foreach (var (ambiguityId, optionId) in decision.AmbiguityResolutions)
        {
            var resolution = NewDecision(stageId, decision.Actor, DecisionKind.OptionChosen,
                $"{ambiguityId} -> {optionId}", cited);
            decisions.Add(resolution);
            events.Append(EventKind.ApprovalDecided, stageId,
                ("decisionId", resolution.Id), ("label", label), ("actor", decision.Actor),
                ("decision", DecisionKind.OptionChosen.ToString()), ("rationale", resolution.Rationale),
                ("ambiguity", ambiguityId), ("option", optionId), ("cites", string.Join(",", cited)));
        }

        return new ApprovalOutcome(decision.Kind, decisions, decision.Rationale, decision.AmbiguityResolutions);
    }

    private Decision NewDecision(string stageId, string actor, DecisionKind kind, string rationale, IReadOnlyList<string> cites) =>
        new($"D{Interlocked.Increment(ref _decisionCounter):000}", stageId, actor, kind, rationale, cites, events.Clock.GetUtcNow());
}

public sealed record ApprovalOutcome(
    DecisionKind Kind,
    IReadOnlyList<Decision> Decisions,
    string Rationale,
    IReadOnlyDictionary<string, string> AmbiguityResolutions);
