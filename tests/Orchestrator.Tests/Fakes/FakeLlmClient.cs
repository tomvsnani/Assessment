using System.Text.Json;
using Orchestrator.Agents.Llm;

namespace Orchestrator.Tests.Fakes;

/// <summary>Scripted model: returns the given turns in order. Records every request it saw.</summary>
public sealed class FakeLlmClient(params LlmMessage.AssistantTurn[] turns) : ILlmClient
{
    private readonly Queue<LlmMessage.AssistantTurn> _turns = new(turns);

    public List<LlmRequest> Requests { get; } = [];

    public string Provider => "fake";

    public string Model => "fake-1";

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        var turn = _turns.Count > 0 ? _turns.Dequeue() : Text("(no more scripted turns)");
        return Task.FromResult(new LlmResponse(turn, turn.ToolCalls.Count > 0 ? "tool_use" : "end_turn", 10, 5));
    }

    public static LlmMessage.AssistantTurn Text(string text) =>
        new(JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { type = "text", text } })).RootElement, text, []);

    public static LlmMessage.AssistantTurn Call(string tool, object input, string id = "call-1") =>
        new(JsonDocument.Parse(JsonSerializer.Serialize(new[] { new { type = "tool_use", id, name = tool, input } })).RootElement,
            string.Empty,
            [new ToolCall(id, tool, JsonDocument.Parse(JsonSerializer.Serialize(input)).RootElement)]);
}
