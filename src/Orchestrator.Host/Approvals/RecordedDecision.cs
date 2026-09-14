using Orchestrator.Core.Contracts;

namespace Orchestrator.Host.Approvals;

/// <summary>One line of <c>recordings/&lt;scenario&gt;/decisions.jsonl</c>: a human decision taken in an interactive run.</summary>
public sealed record RecordedDecision(
    int Seq,
    string Label,
    string StageId,
    string Actor,
    DecisionKind Kind,
    string Rationale,
    IReadOnlyDictionary<string, string> AmbiguityResolutions,
    string ArtifactsReviewed,
    DateTimeOffset DecidedAt);
