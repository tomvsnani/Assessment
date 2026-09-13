using Orchestrator.Core.Contracts;
using Orchestrator.Core.Workflow;

namespace Orchestrator.Core.Governance;

/// <summary>
/// An automatic guardrail evaluated at a gate. Policies are pure functions over the context:
/// no I/O, no LLM, so their verdicts are reproducible and testable.
/// </summary>
public interface IPolicy
{
    /// <summary>Name used in workflow YAML, e.g. <c>no-secrets</c>.</summary>
    string Name { get; }

    PolicyVerdict Evaluate(PolicyContext context);
}

public sealed record PolicyVerdict(PolicyOutcome Outcome, string Reason)
{
    public static readonly PolicyVerdict Pass = new(PolicyOutcome.Pass, "ok");

    public static PolicyVerdict Block(string reason) => new(PolicyOutcome.Block, reason);

    public static PolicyVerdict Warn(string reason) => new(PolicyOutcome.Warn, reason);
}

public enum PolicyOutcome
{
    Pass,
    Warn,
    Block,
}

/// <param name="Outputs">Artifacts produced by the stage being gated (empty at an entry gate).</param>
/// <param name="ChangedFiles">Workspace files the stage created or modified, with their content.</param>
/// <param name="StageRoles">Stage id → agent role, so policies can reason about who did what.</param>
public sealed record PolicyContext(
    StageDefinition Stage,
    IReadOnlyList<Artifact> Outputs,
    IReadOnlyDictionary<string, Artifact> AllArtifacts,
    IReadOnlyList<Decision> Decisions,
    IReadOnlyList<ChangedFile> ChangedFiles,
    IReadOnlyDictionary<string, string> StageRoles);

public sealed record ChangedFile(string Path, string Content);
