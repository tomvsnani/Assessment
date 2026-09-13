namespace Orchestrator.Core.State.Projections;

/// <summary>Current state of every stage, rebuilt from the event log alone.</summary>
public sealed class RunStatus
{
    private readonly Dictionary<string, StageState> _stages = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, StageState> Stages => _stages;

    public bool RunFinished { get; private set; }

    public bool RunSucceeded { get; private set; }

    public static RunStatus From(IEnumerable<RunEvent> events)
    {
        var status = new RunStatus();
        foreach (var evt in events)
        {
            status.Apply(evt);
        }

        return status;
    }

    public void Apply(RunEvent evt)
    {
        switch (evt.Kind)
        {
            case EventKind.StageScheduled:
            case EventKind.StageInvalidated:
                _stages[evt.StageId!] = StageState.Pending;
                break;
            case EventKind.StageStarted:
                _stages[evt.StageId!] = StageState.Running;
                break;
            case EventKind.StageCompleted:
                _stages[evt.StageId!] = StageState.Completed;
                break;
            case EventKind.StageFailed:
                _stages[evt.StageId!] = StageState.Failed;
                break;
            case EventKind.RunCompleted:
                RunFinished = true;
                RunSucceeded = true;
                break;
            case EventKind.RunFailed:
                RunFinished = true;
                RunSucceeded = false;
                break;
            default:
                break;
        }
    }

    public IReadOnlySet<string> Completed =>
        _stages.Where(kv => kv.Value == StageState.Completed).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);

    public IReadOnlySet<string> Running =>
        _stages.Where(kv => kv.Value == StageState.Running).Select(kv => kv.Key).ToHashSet(StringComparer.Ordinal);
}

public enum StageState
{
    Pending,
    Running,
    Completed,
    Failed,
}
