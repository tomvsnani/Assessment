using Orchestrator.Core.Contracts;

namespace Orchestrator.Tests.Fakes;

public sealed class NullTrace : IRunTrace
{
    public void ModelCallStarted(string stageId, string agent, int iteration, int messagesInContext)
    {
    }

    public void ProviderRetry(string stageId, string agent, int status, int attempt, int maxAttempts, TimeSpan delay, string detail)
    {
    }

    public void ToolStarted(string stageId, string agent, string tool, string arguments)
    {
    }

    public void AgentTurn(string stageId, string agent, int iteration, string text, int toolCalls, int inputTokens, int outputTokens)
    {
    }

    public void ToolInvoked(string stageId, string agent, string tool, string arguments, string resultPreview, bool isError, TimeSpan duration)
    {
    }
}
