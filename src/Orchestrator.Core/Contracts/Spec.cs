namespace Orchestrator.Core.Contracts;

/// <summary>
/// The normalized requirement the requirements agent produces: the raw ask turned into an
/// engineering problem with explicit scope, acceptance criteria and a list of things it could
/// not decide on its own. Serialized as JSON into the "spec" artifact.
/// </summary>
public sealed record Spec(
    string Title,
    string ProblemStatement,
    IReadOnlyList<string> InScope,
    IReadOnlyList<string> OutOfScope,
    IReadOnlyList<AcceptanceCriterion> AcceptanceCriteria,
    IReadOnlyList<Ambiguity> Ambiguities,
    IReadOnlyList<string> Assumptions);

/// <summary>Given / When / Then, so it can become a test without translation.</summary>
public sealed record AcceptanceCriterion(string Id, string Given, string When, string Then);

/// <summary>
/// Something the requirement does not settle. The agent proposes options and a recommendation;
/// a human resolves it (or accepts the recommendation) at the approval gate.
/// </summary>
public sealed record Ambiguity(
    string Id,
    string Question,
    IReadOnlyList<AmbiguityOption> Options,
    string RecommendedOptionId,
    string? ResolvedOptionId = null);

public sealed record AmbiguityOption(string Id, string Summary, string TradeOff);
