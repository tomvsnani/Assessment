using Orchestrator.Core.State;

namespace Orchestrator.Core.Governance;

/// <summary>
/// Evaluates the policies a gate names, records every verdict as an event, and reports whether
/// the gate is open. All policies run even after a block so the audit log shows the full picture.
/// </summary>
public sealed class PolicyGate(IReadOnlyDictionary<string, IPolicy> policies, EventStore events)
{
    public PolicyGate(IEnumerable<IPolicy> policies, EventStore events)
        : this(policies.ToDictionary(p => p.Name, p => p, StringComparer.Ordinal), events)
    {
    }

    public GateResult Evaluate(IReadOnlyList<string> policyNames, PolicyContext context, string phase)
    {
        var blocks = new List<string>();
        foreach (var name in policyNames)
        {
            if (!policies.TryGetValue(name, out var policy))
            {
                throw new InvalidOperationException($"Stage '{context.Stage.Id}' names unknown policy '{name}'.");
            }

            var verdict = policy.Evaluate(context);
            events.Append(EventKind.PolicyEvaluated, context.Stage.Id,
                ("policy", name), ("phase", phase), ("verdict", verdict.Outcome.ToString()), ("reason", verdict.Reason));

            if (verdict.Outcome == PolicyOutcome.Block)
            {
                blocks.Add($"{name}: {verdict.Reason}");
            }
        }

        return new GateResult(blocks.Count == 0, blocks);
    }
}

public sealed record GateResult(bool Open, IReadOnlyList<string> Blocks)
{
    public string Summary => Open ? "open" : string.Join("; ", Blocks);
}
