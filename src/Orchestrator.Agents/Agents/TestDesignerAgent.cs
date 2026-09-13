using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Agents;

/// <summary>
/// Independently derives test cases from the spec and plan — in parallel with the implementer and
/// without seeing its code, so the test plan is a second opinion rather than a restatement.
/// </summary>
public sealed class TestDesignerAgent(AgentDependencies deps) : LlmAgent(deps)
{
    public const string ArtifactName = "test-plan";

    public override string Role => "test-designer";

    protected override IReadOnlyList<string> Reads => [RequirementsAgent.ArtifactName, ArchitectAgent.ArtifactName, PlannerAgent.ArtifactName];

    protected override int MaxIterations => 12;

    protected override StageResult Parse(string finalText, StageContext ctx, IReadOnlyDictionary<string, string> artifacts)
    {
        var plan = ArtifactParser.Require(artifacts, ArtifactName);
        return StageResult.Success(Artifact(ArtifactName, ArtifactKind.Tests, plan, ctx, RequirementsAgent.ArtifactName, PlannerAgent.ArtifactName));
    }
}
