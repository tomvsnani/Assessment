using Orchestrator.Core.Contracts;

namespace Orchestrator.Tests.Fakes;

public sealed class NullTrace : IRunTrace
{
    public void AgentTurn(string stageId, string agent, int iteration, string text, int toolCalls, int inputTokens, int outputTokens)
    {
    }

    public void ToolInvoked(string stageId, string agent, string tool, string arguments, string resultPreview, bool isError, TimeSpan duration)
    {
    }
}
