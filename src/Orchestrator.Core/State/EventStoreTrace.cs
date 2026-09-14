using System.Globalization;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Core.State;

/// <summary>Writes agent trace as events so the dashboard, the run directory and replay all see the same thing.</summary>
public sealed class EventStoreTrace(EventStore events) : IRunTrace
{
    /// <summary>Long model outputs are kept whole in the recordings; the event carries enough to read along.</summary>
    public const int MaxTextChars = 4000;

    public void ModelCallStarted(string stageId, string agent, int iteration, int messagesInContext) =>
        events.Append(EventKind.ModelCallStarted, stageId,
            ("agent", agent),
            ("iteration", iteration.ToString(CultureInfo.InvariantCulture)),
            ("messages", messagesInContext.ToString(CultureInfo.InvariantCulture)));

    public void ToolStarted(string stageId, string agent, string tool, string arguments) =>
        events.Append(EventKind.ToolStarted, stageId, ("agent", agent), ("tool", tool), ("arguments", Clip(arguments)));

    public void ProviderRetry(string stageId, string agent, int status, int attempt, int maxAttempts, TimeSpan delay, string detail) =>
        events.Append(EventKind.ProviderRetry, stageId,
            ("agent", agent),
            ("status", status.ToString(CultureInfo.InvariantCulture)),
            ("attempt", attempt.ToString(CultureInfo.InvariantCulture)),
            ("maxAttempts", maxAttempts.ToString(CultureInfo.InvariantCulture)),
            ("delayMs", delay.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)),
            ("detail", Clip(detail)));

    public void AgentTurn(string stageId, string agent, int iteration, string text, int toolCalls, int inputTokens, int outputTokens) =>
        events.Append(EventKind.AgentTurn, stageId,
            ("agent", agent),
            ("iteration", iteration.ToString(CultureInfo.InvariantCulture)),
            ("text", Clip(text)),
            ("toolCalls", toolCalls.ToString(CultureInfo.InvariantCulture)),
            ("inputTokens", inputTokens.ToString(CultureInfo.InvariantCulture)),
            ("outputTokens", outputTokens.ToString(CultureInfo.InvariantCulture)));

    public void ToolInvoked(string stageId, string agent, string tool, string arguments, string resultPreview, bool isError, TimeSpan duration) =>
        events.Append(EventKind.ToolInvoked, stageId,
            ("agent", agent),
            ("tool", tool),
            ("arguments", Clip(arguments)),
            ("result", Clip(resultPreview)),
            ("isError", isError ? "true" : "false"),
            ("durationMs", duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)));

    private static string Clip(string s) => s.Length <= MaxTextChars ? s : s[..MaxTextChars] + "\n… (truncated)";
}
