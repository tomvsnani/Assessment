namespace Orchestrator.Core.Contracts;

/// <summary>
/// One role in the lifecycle (requirements, architect, planner, implementer, ...).
/// An agent may be backed by an LLM or be fully deterministic (the verifier just runs the tests);
/// the runtime does not care which.
/// </summary>
public interface IStageAgent
{
    string Role { get; }

    Task<StageResult> ExecuteAsync(StageContext context);
}

/// <summary>Resolves the role named in a workflow stage to an agent instance.</summary>
public interface IAgentRegistry
{
    IStageAgent Resolve(string role);
}
