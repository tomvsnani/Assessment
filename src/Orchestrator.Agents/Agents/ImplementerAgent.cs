using System.Text;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Agents;

/// <summary>
/// Writes the code and its unit tests, building as it goes. The "implementation" artifact is a
/// summary the agent writes plus the list of files it actually changed (computed by us from the
/// workspace, not trusted from the model).
/// </summary>
public sealed class ImplementerAgent(AgentDependencies deps) : LlmAgent(deps)
{
    public const string ArtifactName = "implementation";

    public override string Role => "implementer";

    protected override IReadOnlyList<string> Reads => [RequirementsAgent.ArtifactName, ArchitectAgent.ArtifactName, PlannerAgent.ArtifactName];

    protected override bool CanWrite => true;

    protected override bool CanBuild => true;

    protected override int MaxIterations => 60;

    protected override string? ExtraContext(StageContext ctx)
    {
        var files = ctx.Workspace.ListFiles();
        return files.Count == 0
            ? "# Workspace\nThe workspace is empty. You are creating the projects from scratch."
            : "# Workspace files\n" + string.Join("\n", files);
    }

    protected override StageResult Parse(string finalText, StageContext ctx, IReadOnlyDictionary<string, string> artifacts)
    {
        var summary = ArtifactParser.Require(artifacts, ArtifactName);
        var changed = ctx.Workspace.ChangedSince(ctx.AttemptStart);
        if (changed.Count == 0)
        {
            throw new AgentOutputException("no files were written to the workspace");
        }

        var sb = new StringBuilder(summary.Trim());
        sb.AppendLine().AppendLine().AppendLine("## Files changed (recorded by the orchestrator)");
        foreach (var path in changed)
        {
            sb.Append("- ").AppendLine(path);
        }

        return StageResult.Success(Artifact(ArtifactName, ArtifactKind.Code, sb.ToString(), ctx,
            RequirementsAgent.ArtifactName, ArchitectAgent.ArtifactName, PlannerAgent.ArtifactName));
    }
}
