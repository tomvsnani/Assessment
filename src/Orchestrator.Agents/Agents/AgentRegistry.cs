using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Agents;

/// <summary>The roles a workflow may name. Adding a role means adding a class here and a prompt file.</summary>
public sealed class AgentRegistry : IAgentRegistry
{
    private readonly Dictionary<string, IStageAgent> _agents;

    public AgentRegistry(AgentDependencies deps)
    {
        IStageAgent[] agents =
        [
            new RequirementsAgent(deps),
            new ArchitectAgent(deps),
            new PlannerAgent(deps),
            new ImplementerAgent(deps),
            new TestDesignerAgent(deps),
            new VerifierAgent(deps.Log),
            new ReviewerAgent(deps),
            new DocWriterAgent(deps),
            new ReleaseManagerAgent(deps),
        ];
        _agents = agents.ToDictionary(a => a.Role, a => a, StringComparer.Ordinal);

        foreach (var role in _agents.Keys.Where(r => r != "verifier" && !deps.Prompts.HasRole(r)))
        {
            throw new InvalidOperationException($"prompts/{role}.md is missing.");
        }
    }

    public IReadOnlyCollection<string> Roles => _agents.Keys;

    public IStageAgent Resolve(string role) =>
        _agents.TryGetValue(role, out var agent)
            ? agent
            : throw new InvalidOperationException($"Unknown agent role '{role}'. Known roles: {string.Join(", ", _agents.Keys.Order(StringComparer.Ordinal))}.");
}
