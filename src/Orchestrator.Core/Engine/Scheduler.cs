using Orchestrator.Core.Contracts;
using Orchestrator.Core.State;
using Orchestrator.Core.Workflow;

namespace Orchestrator.Core.Engine;

/// <summary>
/// Walks the dependency graph: dispatches every stage whose dependencies are complete (up to
/// <see cref="WorkflowDefinition.MaxParallelStages"/> at once), waits at joins, and hands each
/// outcome to the coordinator (re-plan) or the saga (rollback). Loops until every stage is
/// complete, the safe-stop fires, or nothing can make progress.
/// </summary>
public sealed class Scheduler(
    WorkflowDefinition workflow,
    DependencyGraph graph,
    RunState state,
    Executor executor,
    Coordinator coordinator,
    Saga saga,
    SafeStop safeStop,
    EventStore events)
{
    public async Task<RunOutcome> RunAsync(Requirement requirement, IWorkspace workspace)
    {
        events.Append(EventKind.RunStarted, null, ("workflow", workflow.Name), ("kind", workflow.Kind.ToString()), ("requirement", requirement.Id));
        foreach (var stage in graph.Stages)
        {
            events.Append(EventKind.StageScheduled, stage.Id, ("dependsOn", string.Join(",", stage.DependsOn)));
        }

        var inFlight = new Dictionary<Task<StageExecution>, string>();
        // Stages that were running when a re-plan invalidated their inputs; their results are discarded on arrival.
        var stale = new HashSet<string>(StringComparer.Ordinal);

        while (!safeStop.Triggered)
        {
            foreach (var stage in graph.Ready(state.Completed, state.Running))
            {
                if (inFlight.Count >= workflow.MaxParallelStages)
                {
                    break;
                }

                state.MarkRunning(stage.Id);
                var upstream = state.ArtifactSnapshot();
                var decisions = state.DecisionSnapshot();
                inFlight[executor.RunAsync(stage, requirement, upstream, decisions, workspace, safeStop.Token)] = stage.Id;
            }

            if (inFlight.Count == 0)
            {
                break; // nothing running, nothing ready: finished or stuck
            }

            var finished = await Task.WhenAny(inFlight.Keys);
            inFlight.Remove(finished);
            var before = state.ArtifactSnapshot();
            var execution = await finished;

            switch (execution)
            {
                case StageExecution.Completed completed when stale.Remove(completed.StageId):
                    state.MarkFailed(completed.StageId);
                    await saga.CompensateAsync(completed.StageId, safeStop.Token);
                    events.Append(EventKind.StageInvalidated, completed.StageId, ("because", "inputs changed while running"));
                    break;

                case StageExecution.Completed completed:
                    state.MarkCompleted(completed.StageId, completed.Artifacts, completed.Decisions);
                    await coordinator.ReactToArtifactsAsync(completed.StageId, before, completed.Artifacts, safeStop.Token);
                    break;

                case StageExecution.Failed failed:
                    state.MarkFailed(failed.StageId);
                    var replanned = await coordinator.TryRerunFromAsync(graph[failed.StageId], failed.Reason, failed.Artifacts, safeStop.Token);
                    if (!replanned)
                    {
                        safeStop.Trigger($"stage '{failed.StageId}' failed: {failed.Reason}");
                        break;
                    }

                    var rerunFrom = graph[failed.StageId].OnFailure.RerunFrom!;
                    stale.UnionWith(inFlight.Values.Where(id => id == rerunFrom || graph.Downstream(rerunFrom).Contains(id)));

                    break;

                case StageExecution.Rejected rejected:
                    state.MarkFailed(rejected.StageId);
                    safeStop.Trigger($"stage '{rejected.StageId}' rejected by human: {rejected.Rationale}");
                    break;

                case StageExecution.Stopped stopped:
                    state.MarkFailed(stopped.StageId);
                    break;
            }
        }

        await DrainAsync(inFlight.Keys);

        if (safeStop.Triggered)
        {
            await saga.RollbackAsync(safeStop.Reason ?? "safe stop");
            events.Append(EventKind.RunFailed, null, ("reason", safeStop.Reason ?? "safe stop"));
            return new RunOutcome(false, safeStop.Reason ?? "safe stop");
        }

        var allDone = graph.Stages.All(s => state.Completed.Contains(s.Id));
        if (!allDone)
        {
            var stuck = string.Join(", ", graph.Stages.Where(s => !state.Completed.Contains(s.Id)).Select(s => s.Id));
            events.Append(EventKind.RunFailed, null, ("reason", $"no progress possible; incomplete: {stuck}"));
            return new RunOutcome(false, $"incomplete stages: {stuck}");
        }

        events.Append(EventKind.RunCompleted);
        return new RunOutcome(true, "all stages completed");
    }

    private static async Task DrainAsync(IEnumerable<Task<StageExecution>> tasks)
    {
        foreach (var task in tasks)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                // expected after a safe stop
            }
        }
    }
}

public sealed record RunOutcome(bool Succeeded, string Summary);
