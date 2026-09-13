using Orchestrator.Agents.Codebase;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Agents;

/// <summary>Produces the design: components, data flow, schema changes, ADR-style decisions, impacted files.</summary>
public sealed class ArchitectAgent(AgentDependencies deps) : LlmAgent(deps)
{
    public const string ArtifactName = "design";

    public override string Role => "architect";

    protected override IReadOnlyList<string> Reads => [RequirementsAgent.ArtifactName];

    protected override int MaxIterations => 20;

    protected override string? ExtraContext(StageContext ctx) =>
        CodebaseSection(ctx, SeedTerms.FromRequirement(ctx.Requirement.Text));

    protected override StageResult Parse(string finalText, StageContext ctx, IReadOnlyDictionary<string, string> artifacts)
    {
        var design = ArtifactParser.Require(artifacts, ArtifactName);
        if (design.Length < 200)
        {
            throw new AgentOutputException("design is too short to be a design");
        }

        return StageResult.Success(Artifact(ArtifactName, ArtifactKind.Design, design, ctx, RequirementsAgent.ArtifactName));
    }
}
