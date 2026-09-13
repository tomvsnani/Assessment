using Orchestrator.Core.Contracts;

namespace Orchestrator.Core.Engine;

/// <summary>
/// The mutable, in-memory picture of a run that the scheduler owns. Everything here can be
/// rebuilt from the event log; it exists so hot-path decisions do not replay events.
/// Only the scheduler mutates it; executors receive immutable snapshots.
/// </summary>
public sealed class RunState
{
    private readonly Dictionary<string, Artifact> _artifacts = new(StringComparer.Ordinal);
    private readonly List<Decision> _decisions = [];
    private readonly HashSet<string> _completed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _running = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _loops = new(StringComparer.Ordinal);

    public IReadOnlySet<string> Completed => _completed;
    public IReadOnlySet<string> Running => _running;

    public IReadOnlyDictionary<string, Artifact> ArtifactSnapshot() => new Dictionary<string, Artifact>(_artifacts, StringComparer.Ordinal);
    public IReadOnlyList<Decision> DecisionSnapshot() => [.. _decisions];

    public void MarkRunning(string stageId) => _running.Add(stageId);

    public void MarkCompleted(string stageId, IEnumerable<Artifact> artifacts, IEnumerable<Decision> decisions)
    {
        _running.Remove(stageId);
        _completed.Add(stageId);
        foreach (var artifact in artifacts)
        {
            _artifacts[artifact.Name] = artifact;
        }

        _decisions.AddRange(decisions);
    }

    public void MarkFailed(string stageId) => _running.Remove(stageId);

    /// <summary>Forget a stage's completion and everything it produced; it will be scheduled again.</summary>
    public void Invalidate(string stageId)
    {
        _completed.Remove(stageId);
        foreach (var name in _artifacts.Where(kv => kv.Value.ProducedBy == stageId).Select(kv => kv.Key).ToList())
        {
            _artifacts.Remove(name);
        }
    }

    public void AddArtifact(Artifact artifact) => _artifacts[artifact.Name] = artifact;

    public Artifact? Artifact(string name) => _artifacts.GetValueOrDefault(name);

    public int IncrementLoop(string key) => _loops[key] = _loops.GetValueOrDefault(key) + 1;

    public int Loops(string key) => _loops.GetValueOrDefault(key);
}
