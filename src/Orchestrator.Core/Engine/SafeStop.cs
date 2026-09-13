using Orchestrator.Core.State;

namespace Orchestrator.Core.Engine;

/// <summary>
/// The kill switch. Once triggered — by a human rejection, an unrecoverable failure, a policy
/// the run cannot satisfy, or Ctrl+C — no new stage is dispatched and running agents are
/// cancelled. The reason is recorded once; later triggers are ignored.
/// </summary>
public sealed class SafeStop : IDisposable
{
    private readonly CancellationTokenSource _cts;
    private readonly EventStore _events;
    private readonly Lock _gate = new();

    public SafeStop(EventStore events, CancellationToken external = default)
    {
        _events = events;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(external);
        external.Register(() => Trigger("external cancellation (Ctrl+C)"));
    }

    public CancellationToken Token => _cts.Token;

    public bool Triggered { get; private set; }

    public string? Reason { get; private set; }

    public void Trigger(string reason)
    {
        lock (_gate)
        {
            if (Triggered)
            {
                return;
            }

            Triggered = true;
            Reason = reason;
        }

        _events.Append(EventKind.SafeStopTriggered, null, ("reason", reason));
        _cts.Cancel();
    }

    public void Dispose() => _cts.Dispose();
}
