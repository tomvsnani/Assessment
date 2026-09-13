using Orchestrator.Core.Contracts;
using Orchestrator.Core.Governance;

namespace Orchestrator.Cli.Approvals;

/// <summary>
/// The interactive human. Shows the artifacts under review in full, asks for each open
/// ambiguity, then for approve / revise / reject with a rationale. Used in --live runs.
/// </summary>
public sealed class ConsoleApprover(string actor) : IApprover
{
    public Task<ApprovalDecision> DecideAsync(ApprovalRequest request, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine($"══════ APPROVAL REQUIRED: {request.Label} (stage '{request.StageId}') ══════");
        foreach (var artifact in request.ArtifactsToReview)
        {
            Console.WriteLine();
            Console.WriteLine($"── artifact '{artifact.Name}' ({artifact.Kind}, hash {artifact.ContentHash}) ──");
            Console.WriteLine(artifact.Content);
        }

        var resolutions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ambiguity in request.OpenAmbiguities)
        {
            Console.WriteLine();
            Console.WriteLine($"── open ambiguity {ambiguity.Id}: {ambiguity.Question}");
            foreach (var option in ambiguity.Options)
            {
                var marker = option.Id == ambiguity.RecommendedOptionId ? " (recommended)" : string.Empty;
                Console.WriteLine($"   [{option.Id}] {option.Summary}{marker}");
                Console.WriteLine($"        trade-off: {option.TradeOff}");
            }

            var chosen = Ask($"choose option [{ambiguity.RecommendedOptionId}]: ");
            resolutions[ambiguity.Id] = string.IsNullOrWhiteSpace(chosen) ? ambiguity.RecommendedOptionId : chosen.Trim();
        }

        Console.WriteLine();
        DecisionKind kind;
        while (true)
        {
            var answer = Ask("[a]pprove / re[v]ise (send back with notes) / [r]eject (stop the run): ").Trim().ToUpperInvariant();
            kind = answer switch
            {
                "A" => DecisionKind.Approved,
                "V" => DecisionKind.RevisionRequested,
                "R" => DecisionKind.Rejected,
                _ => (DecisionKind)(-1),
            };
            if ((int)kind >= 0)
            {
                break;
            }
        }

        var rationale = Ask(kind == DecisionKind.Approved ? "rationale (optional): " : "what should change / why (required): ").Trim();
        while (kind != DecisionKind.Approved && rationale.Length == 0)
        {
            rationale = Ask("a reason is required: ").Trim();
        }

        return Task.FromResult(new ApprovalDecision(kind, actor, rationale.Length == 0 ? "approved" : rationale, resolutions));
    }

    private static string Ask(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine() ?? string.Empty;
    }
}
