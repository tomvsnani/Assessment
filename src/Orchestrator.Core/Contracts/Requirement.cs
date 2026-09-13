namespace Orchestrator.Core.Contracts;

/// <summary>The input to a run: what a product owner asked for, verbatim.</summary>
public sealed record Requirement(string Id, string Title, string Text, ScenarioKind Kind);

public enum ScenarioKind
{
    Greenfield,
    Brownfield,
    Ambiguous,
}
