using Orchestrator.Core.Workflow;

namespace Orchestrator.Core.Contracts;

/// <summary>
/// Everything an agent may see while executing one stage: the requirement, every artifact
/// produced so far (by name), every decision taken so far, the workspace it may edit, and the
/// checkpoint taken when the stage started (so an agent can list what it changed). Attempts within
/// a stage share the workspace: a retry sees what the previous attempt wrote, so the checkpoint is
/// the stage's, not the attempt's.
/// </summary>
public sealed record StageContext(
    string RunId,
    StageDefinition Stage,
    Requirement Requirement,
    IReadOnlyDictionary<string, Artifact> Artifacts,
    IReadOnlyList<Decision> Decisions,
    IWorkspace Workspace,
    WorkspaceCheckpoint StageStart,
    int Attempt,
    CancellationToken CancellationToken)
{
    public Artifact Require(string artifactName) =>
        Artifacts.TryGetValue(artifactName, out var artifact)
            ? artifact
            : throw new InvalidOperationException($"Stage '{Stage.Id}' needs artifact '{artifactName}' but no upstream stage produced it.");
}
