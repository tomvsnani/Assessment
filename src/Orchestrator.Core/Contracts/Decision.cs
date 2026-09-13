namespace Orchestrator.Core.Contracts;

/// <summary>
/// A recorded choice — by a human at an approval gate, or by an agent resolving an ambiguity.
/// Later stages cite decisions by id, which is how "why did we build it this way" stays answerable.
/// </summary>
public sealed record Decision(
    string Id,
    string StageId,
    string Actor,
    DecisionKind Kind,
    string Rationale,
    IReadOnlyList<string> CitedArtifacts,
    DateTimeOffset At);

public enum DecisionKind
{
    Approved,
    Rejected,
    RevisionRequested,
    OptionChosen,
}
