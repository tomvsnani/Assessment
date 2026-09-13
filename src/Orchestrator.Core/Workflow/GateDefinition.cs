namespace Orchestrator.Core.Workflow;

/// <summary>
/// A checkpoint on the way into or out of a stage.
/// Policies are automatic and named (see <c>Governance/Policies</c>); an approval is a human.
/// </summary>
/// <param name="RequiredArtifacts">Artifact names that must exist for the gate to open.</param>
/// <param name="Policies">Policy names evaluated in order; any <c>Block</c> closes the gate.</param>
/// <param name="Approval">A label for the human decision, e.g. <c>approve-spec</c>; null means no human involved.</param>
public sealed record GateDefinition(
    IReadOnlyList<string> RequiredArtifacts,
    IReadOnlyList<string> Policies,
    string? Approval)
{
    public static readonly GateDefinition Open = new([], [], null);
}
