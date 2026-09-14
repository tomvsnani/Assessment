namespace Orchestrator.Core.Workflow;

/// <summary>
/// The lifecycle graph, loaded from <c>workflows/&lt;name&gt;.yaml</c>. The stages form a directed
/// acyclic graph through <see cref="StageDefinition.DependsOn"/>. A workflow says nothing about
/// which requirement or workspace it runs on; that is the run's business.
/// </summary>
public sealed record WorkflowDefinition(
    string Name,
    int MaxParallelStages,
    IReadOnlyList<StageDefinition> Stages);
