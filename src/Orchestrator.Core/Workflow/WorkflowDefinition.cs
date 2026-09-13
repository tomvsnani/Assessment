using Orchestrator.Core.Contracts;

namespace Orchestrator.Core.Workflow;

/// <summary>
/// One scenario's lifecycle, loaded from <c>workflows/&lt;name&gt;.yaml</c>. The stages form a
/// directed acyclic graph through <see cref="StageDefinition.DependsOn"/>.
/// </summary>
/// <param name="Baseline">
/// What the workspace starts from: <c>scaffold</c> (build configuration only) for greenfield, or <c>git:&lt;ref&gt;</c> to
/// materialise a tagged version of this repository for brownfield work.
/// </param>
public sealed record WorkflowDefinition(
    string Name,
    ScenarioKind Kind,
    string RequirementPath,
    string Baseline,
    int MaxParallelStages,
    IReadOnlyList<StageDefinition> Stages);
