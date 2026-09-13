using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Agents;

/// <summary>Writes the user-facing documentation for the change into the workspace and returns it as an artifact.</summary>
public sealed class DocWriterAgent(AgentDependencies deps) : LlmAgent(deps)
{
    public const string ArtifactName = "documentation";

    public override string Role => "doc-writer";

    protected override IReadOnlyList<string> Reads =>
        [RequirementsAgent.ArtifactName, ArchitectAgent.ArtifactName, ImplementerAgent.ArtifactName];

    protected override bool CanWrite => true;

    protected override int MaxIterations => 20;

    protected override StageResult Parse(string finalText, StageContext ctx, IReadOnlyDictionary<string, string> artifacts)
    {
        var docs = ArtifactParser.Require(artifacts, ArtifactName);
        return StageResult.Success(Artifact(ArtifactName, ArtifactKind.Documentation, docs, ctx, ArchitectAgent.ArtifactName, ImplementerAgent.ArtifactName));
    }
}
