using Orchestrator.Core.Contracts;
using Orchestrator.Core.Governance;
using Orchestrator.Core.Workflow;

namespace Orchestrator.Agents.Tools;

/// <summary>
/// Runs the file-content policies (no secrets, no PII in logs) on a single file the moment an
/// agent writes it, and phrases the verdict as a warning for the tool result. The exit gate is
/// still the enforcement point; this exists because a block discovered there arrives after the
/// whole attempt's work — seen live: 46 turns, then <c>pii-in-logs</c> failed the stage and the run.
/// Telling the agent in the next turn costs one sentence.
/// </summary>
public sealed class FilePolicyCheck(IReadOnlyList<IPolicy> policies, StageDefinition stage)
{
    public string? WarningFor(string path, string content)
    {
        var context = new PolicyContext(stage, [], new Dictionary<string, Artifact>(StringComparer.Ordinal), [], [new ChangedFile(path, content)], new Dictionary<string, string>(StringComparer.Ordinal));
        var blocks = policies
            .Select(p => (p.Name, Verdict: p.Evaluate(context)))
            .Where(r => r.Verdict.Outcome == PolicyOutcome.Block)
            .Select(r => $"policy '{r.Name}' will BLOCK this stage at the exit gate: {r.Verdict.Reason}")
            .ToList();
        return blocks.Count == 0 ? null : "WARNING — " + string.Join("; ", blocks) + ". Fix it before you finish.";
    }
}
