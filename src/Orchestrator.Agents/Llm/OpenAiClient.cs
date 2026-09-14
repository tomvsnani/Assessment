using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orchestrator.Agents.Llm;

/// <summary>Raw HTTP adapter for OpenAI Chat Completions with function calling.</summary>
public sealed class OpenAiClient(HttpClient http, string apiKey, string model = OpenAiClient.DefaultModel) : ILlmClient
{
    public const string DefaultModel = "gpt-5";
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";

    public string Provider => "openai";

    public string Model => model;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var messages = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = request.System });
        foreach (var message in request.Messages)
        {
            foreach (var wire in ToWire(message))
            {
                messages.Add(wire);
            }
        }

        var body = new JsonObject
        {
            ["model"] = model,
            ["max_completion_tokens"] = request.MaxTokens,
            ["messages"] = messages,
        };
        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(request.Tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = JsonNode.Parse(t.InputSchema.GetRawText()),
                },
            }).ToArray());
        }

        using var message2 = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(body) };
        message2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await http.SendAsync(message2, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new LlmException(Provider, (int)response.StatusCode, json);
        }

        return Parse(JsonDocument.Parse(json).RootElement);
    }

    private static IEnumerable<JsonNode> ToWire(LlmMessage message)
    {
        switch (message)
        {
            case LlmMessage.UserText user:
                yield return new JsonObject { ["role"] = "user", ["content"] = user.Text };
                break;
            case LlmMessage.AssistantTurn assistant:
                yield return JsonNode.Parse(assistant.RawContent.GetRawText())!; // the full assistant message object
                break;
            case LlmMessage.ToolResults results:
                foreach (var r in results.Results)
                {
                    yield return new JsonObject { ["role"] = "tool", ["tool_call_id"] = r.CallId, ["content"] = r.Content };
                }

                break;
        }
    }

    private static LlmResponse Parse(JsonElement root)
    {
        var choice = root.GetProperty("choices")[0];
        var assistant = choice.GetProperty("message");
        var text = assistant.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : string.Empty;
        var calls = new List<ToolCall>();
        if (assistant.TryGetProperty("tool_calls", out var toolCalls))
        {
            foreach (var call in toolCalls.EnumerateArray())
            {
                var fn = call.GetProperty("function");
                var args = fn.GetProperty("arguments").GetString() ?? "{}";
                calls.Add(new ToolCall(call.GetProperty("id").GetString()!, fn.GetProperty("name").GetString()!, JsonDocument.Parse(args).RootElement.Clone()));
            }
        }

        var usage = root.GetProperty("usage");
        var finish = choice.GetProperty("finish_reason").GetString() ?? "stop";
        return new LlmResponse(
            new LlmMessage.AssistantTurn(assistant.Clone(), text, calls),
            finish == "tool_calls" ? "tool_use" : finish == "length" ? "max_tokens" : "end_turn",
            usage.GetProperty("prompt_tokens").GetInt32(),
            usage.GetProperty("completion_tokens").GetInt32());
    }
}
