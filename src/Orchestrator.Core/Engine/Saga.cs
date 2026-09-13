using Orchestrator.Core.State;

namespace Orchestrator.Core.Engine;

/// <summary>
/// Compensation registry. Every completed stage registers how to undo its side effects
/// (restore the workspace checkpoint, drop its artifacts). Rollback runs them newest-first,
/// keeps going if one fails, and records each step so the audit log shows exactly what was undone.
/// </summary>
public sealed class Saga(EventStore events)
{
    private readonly List<(string StageId, Func<CancellationToken, Task> Compensate)> _steps = [];
    private readonly Lock _gate = new();

    public void Register(string stageId, Func<CancellationToken, Task> compensate)
    {
        lock (_gate)
        {
            _steps.Add((stageId, compensate));
        }
    }

    /// <summary>Undo one stage (used when the coordinator invalidates it) and forget its step.</summary>
    public async Task CompensateAsync(string stageId, CancellationToken ct)
    {
        List<(string StageId, Func<CancellationToken, Task> Compensate)> mine;
        lock (_gate)
        {
            mine = _steps.Where(s => s.StageId == stageId).ToList();
            _steps.RemoveAll(s => s.StageId == stageId);
        }

        foreach (var step in Enumerable.Reverse(mine))
        {
            await RunStepAsync(step.StageId, step.Compensate, ct);
        }
    }

    /// <summary>Undo everything, newest first. Uses a fresh token: a rollback must not be cancelled by the stop that caused it.</summary>
    public async Task RollbackAsync(string reason)
    {
        List<(string StageId, Func<CancellationToken, Task> Compensate)> all;
        lock (_gate)
        {
            all = [.. _steps];
            _steps.Clear();
        }

        foreach (var step in Enumerable.Reverse(all))
        {
            await RunStepAsync(step.StageId, step.Compensate, CancellationToken.None);
        }

        events.Append(EventKind.RollbackCompleted, null, ("reason", reason), ("stagesUndone", all.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private async Task RunStepAsync(string stageId, Func<CancellationToken, Task> compensate, CancellationToken ct)
    {
        try
        {
            await compensate(ct);
            events.Append(EventKind.CompensationRun, stageId, ("outcome", "ok"));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            events.Append(EventKind.CompensationRun, stageId, ("outcome", "failed"), ("error", e.Message));
        }
    }
}
