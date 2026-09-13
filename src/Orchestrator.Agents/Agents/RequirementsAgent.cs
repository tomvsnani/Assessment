using Orchestrator.Agents.Codebase;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Agents;

/// <summary>Turns the raw ask into a <see cref="Spec"/>: scope, Given/When/Then criteria, explicit ambiguities.</summary>
public sealed class RequirementsAgent(AgentDependencies deps) : LlmAgent(deps)
{
    public const string ArtifactName = "spec";

    public override string Role => "requirements";

    protected override IReadOnlyList<string> Reads => [];

    protected override int MaxIterations => 15;

    protected override string? ExtraContext(StageContext ctx) =>
        CodebaseSection(ctx, SeedTerms.FromRequirement(ctx.Requirement.Text));

    protected override StageResult Parse(string finalText, StageContext ctx, IReadOnlyDictionary<string, string> artifacts)
    {
        var json = ArtifactParser.Require(artifacts, ArtifactName);
        Spec spec;
        try
        {
            spec = ArtifactJson.Deserialize<Spec>(json);
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new AgentOutputException($"spec is not valid Spec JSON: {e.Message}");
        }

        if (spec.AcceptanceCriteria.Count == 0)
        {
            throw new AgentOutputException("spec has no acceptance criteria");
        }

        // Re-serialise so the stored artifact is canonical regardless of how the model formatted it.
        return StageResult.Success(Artifact(ArtifactName, ArtifactKind.Spec, ArtifactJson.Serialize(spec), ctx));
    }
}
