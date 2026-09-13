using Orchestrator.Core.Contracts;

namespace Orchestrator.Core.Governance.Policies;

/// <summary>
/// The role that reviews code must not be the role that wrote it. Enforced at the entry of any
/// stage whose gate names this policy (the review stage), by comparing agent roles.
/// </summary>
public sealed class SegregationOfDutiesPolicy : IPolicy
{
    public const string ImplementationArtifact = "implementation";

    public string Name => "segregation-of-duties";

    public PolicyVerdict Evaluate(PolicyContext context)
    {
        if (!context.AllArtifacts.TryGetValue(ImplementationArtifact, out var implementation))
        {
            return PolicyVerdict.Pass; // nothing to review yet
        }

        var implementerRole = context.StageRoles.GetValueOrDefault(implementation.ProducedBy);
        var reviewerRole = context.Stage.Agent;

        return string.Equals(implementerRole, reviewerRole, StringComparison.Ordinal)
            ? PolicyVerdict.Block($"role '{reviewerRole}' would review its own implementation")
            : PolicyVerdict.Pass;
    }
}
