using System.Text.Json;

namespace Orchestrator.Agents.Llm;

/// <summary>
/// Provider-neutral conversation model. User text and tool results are plain records; an
/// assistant turn keeps the provider's raw content so it can be echoed back verbatim on the next
/// request (Anthropic thinking blocks, OpenAI tool_call ids, Gemini function parts all survive).
/// </summary>
public abstract record LlmMessage
{
    public sealed record UserText(string Text) : LlmMessage;

    public sealed record ToolResults(IReadOnlyList<ToolResult> Results) : LlmMessage;

    public sealed record AssistantTurn(JsonElement RawContent, string Text, IReadOnlyList<ToolCall> ToolCalls) : LlmMessage;
}

public sealed record ToolCall(string Id, string Name, JsonElement Input);

public sealed record ToolResult(string CallId, string Name, string Content, bool IsError = false);

public sealed record ToolDefinition(string Name, string Description, JsonElement InputSchema);

public sealed record LlmRequest(
    string System,
    IReadOnlyList<LlmMessage> Messages,
    IReadOnlyList<ToolDefinition> Tools,
    int MaxTokens = 16000);

public sealed record LlmResponse(
    LlmMessage.AssistantTurn Turn,
    string StopReason,
    int InputTokens,
    int OutputTokens)
{
    public bool WantsTools => Turn.ToolCalls.Count > 0;
}

public interface ILlmClient
{
    string Provider { get; }

    string Model { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}
