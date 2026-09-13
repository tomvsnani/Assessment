using Orchestrator.Core.Contracts;

namespace Orchestrator.Tests.Fakes;

/// <summary>Registry of delegate-backed agents so a test can script exactly what each stage does.</summary>
public sealed class FakeAgents : IAgentRegistry
{
    private readonly Dictionary<string, IStageAgent> _agents = new(StringComparer.Ordinal);

    public FakeAgents Add(string role, Func<StageContext, Task<StageResult>> behaviour)
    {
        _agents[role] = new DelegateAgent(role, behaviour);
        return this;
    }

    /// <summary>An agent that always produces one artifact with the given name and content.</summary>
    public FakeAgents Producing(string role, string artifactName, ArtifactKind kind = ArtifactKind.Documentation, string? content = null) =>
        Add(role, ctx => Task.FromResult(StageResult.Success(
            new Artifact(artifactName, kind, content ?? $"{artifactName} by {role}", ctx.Stage.Id, []))));

    public IStageAgent Resolve(string role) =>
        _agents.TryGetValue(role, out var agent) ? agent : throw new InvalidOperationException($"No fake agent for role '{role}'.");

    private sealed class DelegateAgent(string role, Func<StageContext, Task<StageResult>> behaviour) : IStageAgent
    {
        public string Role => role;

        public Task<StageResult> ExecuteAsync(StageContext context) => behaviour(context);
    }
}
