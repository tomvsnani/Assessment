using Orchestrator.Core.Contracts;
using Orchestrator.Core.State;
using Orchestrator.Core.Workflow;

namespace Orchestrator.Core.Engine;

/// <summary>
/// Re-planning. Two triggers:
/// <list type="number">
/// <item>A stage failed and its workflow says <c>on_failure.rerun_from</c>: the earlier stage and
/// everything downstream are invalidated and re-run with the failure as feedback (e.g. tests
/// failed → implement again).</item>
/// <item>A completed stage produced an artifact whose content differs from the version downstream
/// stages consumed (e.g. a re-run requirements stage changed the spec): those downstream stages
/// are invalidated so nothing is built on a stale input.</item>
/// </list>
/// Invalidation compensates the affected stages (workspace restored, artifacts dropped) and
/// records exactly what was invalidated and why.
/// </summary>
public sealed class Coordinator(DependencyGraph graph, RunState state, Saga saga, EventStore events)
{
    public async Task<bool> TryRerunFromAsync(StageDefinition failedStage, string reason, IReadOnlyList<Artifact> feedback, bool mayReplan, CancellationToken ct)
    {
        var handling = failedStage.OnFailure;
        if (handling.RerunFrom is null)
        {
            return false;
        }

        if (!mayReplan)
        {
            // A malformed output is not a judgement about the upstream work; looping back would send the implementer noise.
            events.Append(EventKind.ReplanTriggered, failedStage.Id,
                ("cause", $"not a verdict: {reason}"), ("loop", $"{handling.RerunFrom}->{failedStage.Id}"), ("invalidated", ""), ("accepted", "false"));
            return false;
        }

        var loopKey = $"{handling.RerunFrom}->{failedStage.Id}";
        if (state.Loops(loopKey) >= handling.MaxLoops)
        {
            events.Append(EventKind.ReplanTriggered, failedStage.Id,
                ("cause", "loop budget exhausted"), ("loop", loopKey), ("invalidated", ""), ("accepted", "false"));
            return false;
        }

        var loop = state.IncrementLoop(loopKey);
        var earlier = state.Artifact(Executor.FeedbackArtifact); // read before invalidation drops it
        var invalidated = await InvalidateAsync(handling.RerunFrom, includeSelf: true, ct);
        foreach (var artifact in feedback)
        {
            state.AddArtifact(Accumulate(earlier, artifact with { Name = Executor.FeedbackArtifact, ProducedBy = failedStage.Id }));
        }

        events.Append(EventKind.ReplanTriggered, failedStage.Id,
            ("cause", reason), ("rerunFrom", handling.RerunFrom), ("loop", $"{loop}/{handling.MaxLoops}"),
            ("invalidated", string.Join(",", invalidated)), ("accepted", "true"));
        return true;
    }

    /// <summary>Called after every completion. Returns the stages invalidated because their inputs changed.</summary>
    public async Task<IReadOnlyList<string>> ReactToArtifactsAsync(
        string producerStageId,
        IReadOnlyDictionary<string, Artifact> before,
        IReadOnlyList<Artifact> produced,
        CancellationToken ct)
    {
        var changed = produced
            .Where(a => before.TryGetValue(a.Name, out var old) && old.ContentHash != a.ContentHash)
            .Select(a => a.Name)
            .ToList();
        if (changed.Count == 0)
        {
            return [];
        }

        var stale = graph.Downstream(producerStageId).Where(state.Completed.Contains).ToList();
        if (stale.Count == 0)
        {
            return [];
        }

        var invalidated = new List<string>();
        foreach (var stageId in stale)
        {
            invalidated.AddRange(await InvalidateAsync(stageId, includeSelf: true, ct));
        }

        invalidated = invalidated.Distinct(StringComparer.Ordinal).ToList();
        events.Append(EventKind.ReplanTriggered, producerStageId,
            ("cause", $"upstream artifact(s) changed: {string.Join(", ", changed)}"),
            ("invalidated", string.Join(",", invalidated)), ("accepted", "true"));
        return invalidated;
    }

    /// <summary>
    /// Feedback from successive re-plans is concatenated, newest last, so a build failure on the
    /// second pass does not make the implementer forget the reviewer's findings from the first.
    /// </summary>
    private static Artifact Accumulate(Artifact? earlier, Artifact latest)
    {
        if (earlier is null || earlier.ProducedBy == latest.ProducedBy)
        {
            return latest;
        }

        var combined = $"## Earlier feedback (from stage '{earlier.ProducedBy}') — still applies unless addressed\n\n{earlier.Content.Trim()}\n\n## Latest feedback (from stage '{latest.ProducedBy}')\n\n{latest.Content.Trim()}";
        return latest with { Content = combined };
    }

    private async Task<IReadOnlyList<string>> InvalidateAsync(string stageId, bool includeSelf, CancellationToken ct)
    {
        var targets = graph.Downstream(stageId).ToList();
        if (includeSelf)
        {
            targets.Add(stageId);
        }

        // Newest first so compensations unwind in reverse order of completion.
        var ordered = graph.TopologicalOrder.Where(targets.Contains).Reverse().ToList();
        var invalidated = new List<string>();
        foreach (var id in ordered)
        {
            if (!state.Completed.Contains(id))
            {
                continue;
            }

            await saga.CompensateAsync(id, ct);
            state.Invalidate(id);
            events.Append(EventKind.StageInvalidated, id, ("because", stageId));
            invalidated.Add(id);
        }

        return invalidated;
    }
}
