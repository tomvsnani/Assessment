namespace Orchestrator.Core.Contracts;

/// <summary>The input to a run: what a product owner asked for, verbatim.</summary>
public sealed record Requirement(string Id, string Title, string Text, ScenarioKind Kind);

/// <summary>Greenfield when the workspace starts without code, brownfield otherwise. Ambiguity is not a kind: the requirements agent finds it.</summary>
public enum ScenarioKind
{
    Greenfield,
    Brownfield,
}
