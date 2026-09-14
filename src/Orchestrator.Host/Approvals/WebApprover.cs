using Orchestrator.Core.Contracts;
using Orchestrator.Core.Governance;

namespace Orchestrator.Host.Approvals;

/// <summary>
/// The human in the dashboard. The executor's thread parks on a <see cref="TaskCompletionSource{T}"/>
/// until <c>POST /api/runs/{id}/approval</c> supplies a decision. Only one gate can be pending per run.
/// </summary>
public sealed class WebApprover : IApprover
{
    private readonly Lock _gate = new();
    private TaskCompletionSource<ApprovalDecision>? _pending;

    public ApprovalRequest? Pending { get; private set; }

    public Task<ApprovalDecision> DecideAsync(ApprovalRequest request, CancellationToken ct)
    {
        TaskCompletionSource<ApprovalDecision> tcs;
        lock (_gate)
        {
            if (_pending is not null)
            {
                throw new InvalidOperationException("Another approval is already pending.");
            }

            tcs = new TaskCompletionSource<ApprovalDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pending = tcs;
            Pending = request;
        }

        ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task.ContinueWith(t =>
        {
            lock (_gate)
            {
                _pending = null;
                Pending = null;
            }

            return t.GetAwaiter().GetResult();
        }, TaskScheduler.Default);
    }

    /// <summary>Returns false when nothing is waiting (the gate was already decided, or none is open).</summary>
    public bool TryResolve(ApprovalDecision decision)
    {
        lock (_gate)
        {
            return _pending?.TrySetResult(decision) == true;
        }
    }
}
