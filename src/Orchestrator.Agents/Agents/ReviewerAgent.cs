using System.Text.RegularExpressions;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Agents;

/// <summary>
/// Code review by a different role than the implementer (enforced by the segregation-of-duties
/// policy). REQUEST_CHANGES fails the stage; the workflow sends the findings back to the implementer.
/// </summary>
public sealed partial class ReviewerAgent(AgentDependencies deps) : LlmAgent(deps)
{
    public const string ArtifactName = "review";

    public override string Role => "reviewer";


    protected override string OwnedArtifact => ArtifactName;

    protected override IReadOnlyList<string> Reads =>
        [RequirementsAgent.ArtifactName, ArchitectAgent.ArtifactName, ImplementerAgent.ArtifactName, TestDesignerAgent.ArtifactName, VerifierAgent.ArtifactName];

    protected override int MaxIterations => 25;

    protected override StageResult Parse(string finalText, StageContext ctx, IReadOnlyDictionary<string, string> artifacts)
    {
        var review = ArtifactParser.Require(artifacts, ArtifactName);
        var verdict = Verdict().Match(review);
        if (!verdict.Success)
        {
            throw new AgentOutputException("review must contain a line 'VERDICT: APPROVE' or 'VERDICT: REQUEST_CHANGES'");
        }

        var artifact = Artifact(ArtifactName, ArtifactKind.Review, review, ctx, ImplementerAgent.ArtifactName, VerifierAgent.ArtifactName);
        if (verdict.Groups["v"].Value == "APPROVE")
        {
            return StageResult.Success(artifact);
        }

        return StageResult.Failure("reviewer requested changes",
            new Artifact("feedback", ArtifactKind.Feedback, "A reviewer examined your implementation and requested changes:\n\n" + review, ctx.Stage.Id, [ImplementerAgent.ArtifactName]),
            artifact);
    }

    [GeneratedRegex(@"^\s*VERDICT:\s*(?<v>APPROVE|REQUEST_CHANGES)\s*$", RegexOptions.Multiline)]
    private static partial Regex Verdict();
}
