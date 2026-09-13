using Orchestrator.Core.Workflow;

namespace Orchestrator.Core.Contracts;

/// <summary>
/// Everything an agent may see while executing one stage: the requirement, every artifact
/// produced so far (by name), every decision taken so far, the workspace it may edit, and the
/// checkpoint taken just before this attempt (so an agent can list what it changed).
/// </summary>
public sealed record StageContext(
    string RunId,
    StageDefinition Stage,
    Requirement Requirement,
    IReadOnlyDictionary<string, Artifact> Artifacts,
    IReadOnlyList<Decision> Decisions,
    IWorkspace Workspace,
    WorkspaceCheckpoint AttemptStart,
    int Attempt,
    CancellationToken CancellationToken)
{
    public Artifact Require(string artifactName) =>
        Artifacts.TryGetValue(artifactName, out var artifact)
            ? artifact
            : throw new InvalidOperationException($"Stage '{Stage.Id}' needs artifact '{artifactName}' but no upstream stage produced it.");
}
