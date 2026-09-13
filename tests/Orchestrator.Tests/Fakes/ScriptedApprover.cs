using Orchestrator.Core.Contracts;
using Orchestrator.Core.Governance;

namespace Orchestrator.Tests.Fakes;

/// <summary>Returns decisions in the order given; approves everything once the script runs out.</summary>
public sealed class ScriptedApprover(params ApprovalDecision[] script) : IApprover
{
    private readonly Queue<ApprovalDecision> _script = new(script);

    public List<ApprovalRequest> Requests { get; } = [];

    public Task<ApprovalDecision> DecideAsync(ApprovalRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return Task.FromResult(_script.TryDequeue(out var next) ? next : ApprovalDecision.Approve("test-approver"));
    }

    public static ApprovalDecision Reject(string why) =>
        new(DecisionKind.Rejected, "test-approver", why, new Dictionary<string, string>(StringComparer.Ordinal));

    public static ApprovalDecision Revise(string why) =>
        new(DecisionKind.RevisionRequested, "test-approver", why, new Dictionary<string, string>(StringComparer.Ordinal));

    public static ApprovalDecision ApproveResolving(string ambiguityId, string optionId) =>
        new(DecisionKind.Approved, "test-approver", "approved with resolution",
            new Dictionary<string, string>(StringComparer.Ordinal) { [ambiguityId] = optionId });
}
