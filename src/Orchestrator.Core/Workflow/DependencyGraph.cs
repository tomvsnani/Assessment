namespace Orchestrator.Core.Workflow;

/// <summary>
/// The stage DAG. Validates on construction (unknown dependencies, duplicates, cycles) and answers
/// the two questions the scheduler asks: "what can run now?" and "what is downstream of X?".
/// </summary>
public sealed class DependencyGraph
{
    private readonly Dictionary<string, StageDefinition> _stages;
    private readonly Dictionary<string, List<string>> _dependents;

    public DependencyGraph(IReadOnlyList<StageDefinition> stages)
    {
        _stages = new Dictionary<string, StageDefinition>(StringComparer.Ordinal);
        foreach (var stage in stages)
        {
            if (!_stages.TryAdd(stage.Id, stage))
            {
                throw new WorkflowValidationException($"Stage id '{stage.Id}' is declared twice.");
            }
        }

        _dependents = _stages.Keys.ToDictionary(id => id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var stage in stages)
        {
            foreach (var dependency in stage.DependsOn)
            {
                if (!_stages.ContainsKey(dependency))
                {
                    throw new WorkflowValidationException($"Stage '{stage.Id}' depends on unknown stage '{dependency}'.");
                }

                _dependents[dependency].Add(stage.Id);
            }
        }

        TopologicalOrder = ComputeTopologicalOrder();
    }

    public IReadOnlyList<string> TopologicalOrder { get; }

    public StageDefinition this[string id] => _stages[id];

    public IEnumerable<StageDefinition> Stages => TopologicalOrder.Select(id => _stages[id]);

    /// <summary>Stages whose dependencies are all complete and which are not themselves complete or running.</summary>
    public IReadOnlyList<StageDefinition> Ready(IReadOnlySet<string> completed, IReadOnlySet<string> running) =>
        Stages
            .Where(s => !completed.Contains(s.Id) && !running.Contains(s.Id))
            .Where(s => s.DependsOn.All(completed.Contains))
            .ToList();

    /// <summary>Every stage that transitively depends on the given one. Used for invalidation and re-planning.</summary>
    public IReadOnlySet<string> Downstream(string stageId)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(_dependents[stageId]);
        while (queue.TryDequeue(out var next))
        {
            if (result.Add(next))
            {
                foreach (var dependent in _dependents[next])
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        return result;
    }

    /// <summary>Groups of stages that may run concurrently, in order. Only used for display.</summary>
    public IReadOnlyList<IReadOnlyList<string>> ParallelLevels()
    {
        var level = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var id in TopologicalOrder)
        {
            level[id] = _stages[id].DependsOn.Count == 0 ? 0 : _stages[id].DependsOn.Max(d => level[d]) + 1;
        }

        return level.GroupBy(kv => kv.Value).OrderBy(g => g.Key)
            .Select(g => (IReadOnlyList<string>)g.Select(kv => kv.Key).ToList()).ToList();
    }

    private List<string> ComputeTopologicalOrder()
    {
        // Kahn's algorithm; anything left with in-degree > 0 at the end is part of a cycle.
        var inDegree = _stages.ToDictionary(kv => kv.Key, kv => kv.Value.DependsOn.Count, StringComparer.Ordinal);
        var ready = new Queue<string>(_stages.Keys.Where(id => inDegree[id] == 0).OrderBy(id => id, StringComparer.Ordinal));
        var order = new List<string>();

        while (ready.TryDequeue(out var id))
        {
            order.Add(id);
            foreach (var dependent in _dependents[id])
            {
                if (--inDegree[dependent] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }

        if (order.Count != _stages.Count)
        {
            var cyclic = string.Join(", ", _stages.Keys.Except(order).OrderBy(id => id, StringComparer.Ordinal));
            throw new WorkflowValidationException($"Workflow has a cycle involving: {cyclic}.");
        }

        return order;
    }
}

public sealed class WorkflowValidationException(string message) : Exception(message);
