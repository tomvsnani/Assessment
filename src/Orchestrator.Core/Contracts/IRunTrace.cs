namespace Orchestrator.Core.Contracts;

/// <summary>
/// How agents report what they are doing while a stage runs — each model turn and each tool call.
/// Separate from the stage lifecycle events the executor emits: this is the fine-grained view a
/// dashboard shows live, not what the audit log keeps.
/// </summary>
public interface IRunTrace
{
    /// <summary>A request is on its way to the model; the dashboard shows "thinking…" until the matching turn arrives.</summary>
    void ModelCallStarted(string stageId, string agent, int iteration, int messagesInContext);

    void AgentTurn(string stageId, string agent, int iteration, string text, int toolCalls, int inputTokens, int outputTokens);

    /// <summary>A tool is about to run (builds and tests take a while).</summary>
    void ToolStarted(string stageId, string agent, string tool, string arguments);

    void ToolInvoked(string stageId, string agent, string tool, string arguments, string resultPreview, bool isError, TimeSpan duration);
}
