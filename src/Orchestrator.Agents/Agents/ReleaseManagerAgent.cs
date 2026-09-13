using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Agents;

/// <summary>
/// Produces the change record a change-advisory board would read: risk rating, blast radius,
/// rollback plan, verification evidence, go-live checklist. The human approves or rejects it.
/// </summary>
public sealed class ReleaseManagerAgent(AgentDependencies deps) : LlmAgent(deps)
{
    public const string ArtifactName = "change-record";

    public override string Role => "release-manager";

    protected override IReadOnlyList<string> Reads =>
        [RequirementsAgent.ArtifactName, ArchitectAgent.ArtifactName, ImplementerAgent.ArtifactName, VerifierAgent.ArtifactName, ReviewerAgent.ArtifactName, DocWriterAgent.ArtifactName];

    protected override int MaxIterations => 10;

    protected override StageResult Parse(string finalText, StageContext ctx, IReadOnlyDictionary<string, string> artifacts)
    {
        var json = ArtifactParser.Require(artifacts, ArtifactName);
        ChangeRecord record;
        try
        {
            record = ArtifactJson.Deserialize<ChangeRecord>(json);
        }
        catch (System.Text.Json.JsonException e)
        {
            throw new AgentOutputException($"change-record is not valid ChangeRecord JSON: {e.Message}");
        }

        if (string.IsNullOrWhiteSpace(record.RollbackPlan))
        {
            throw new AgentOutputException("change-record has no rollback plan");
        }

        return StageResult.Success(Artifact(ArtifactName, ArtifactKind.ChangeRecord, ArtifactJson.Serialize(record), ctx,
            ImplementerAgent.ArtifactName, VerifierAgent.ArtifactName, ReviewerAgent.ArtifactName));
    }
}

/// <summary>The change-control document. Shaped so it could be pasted into a CAB ticket.</summary>
public sealed record ChangeRecord(
    string Title,
    RiskLevel RiskRating,
    string RiskRationale,
    IReadOnlyList<string> BlastRadius,
    string RollbackPlan,
    IReadOnlyList<string> VerificationEvidence,
    IReadOnlyList<string> GoLiveChecklist,
    IReadOnlyList<string> OpenRisks);
