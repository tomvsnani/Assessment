using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orchestrator.Agents.Llm;

/// <summary>Raw HTTP adapter for the Gemini generateContent API with function declarations.</summary>
public sealed class GeminiClient(HttpClient http, string apiKey, string model = GeminiClient.DefaultModel) : ILlmClient
{
    /// <summary>The Flash tier has the widest free quota; Pro models are quota-blocked on free keys.</summary>
    public const string DefaultModel = "gemini-2.5-flash";

    public string Provider => "gemini";

    public string Model => model;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var contents = new JsonArray();
        foreach (var message in request.Messages)
        {
            contents.Add(ToWire(message));
        }

        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = request.System }) },
            ["contents"] = contents,
            ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = request.MaxTokens },
        };
        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(new JsonObject
            {
                ["functionDeclarations"] = new JsonArray(request.Tools.Select(t => (JsonNode)new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = JsonNode.Parse(t.InputSchema.GetRawText()),
                }).ToArray()),
            });
        }

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
        using var message2 = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        message2.Headers.Add("x-goog-api-key", apiKey);

        using var response = await http.SendAsync(message2, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new LlmException(Provider, (int)response.StatusCode, json);
        }

        return Parse(JsonDocument.Parse(json).RootElement);
    }

    private static JsonNode ToWire(LlmMessage message) => message switch
    {
        LlmMessage.UserText user => new JsonObject { ["role"] = "user", ["parts"] = new JsonArray(new JsonObject { ["text"] = user.Text }) },
        LlmMessage.AssistantTurn assistant => JsonNode.Parse(assistant.RawContent.GetRawText())!,
        LlmMessage.ToolResults results => new JsonObject
        {
            ["role"] = "user",
            ["parts"] = new JsonArray(results.Results.Select(r => (JsonNode)new JsonObject
            {
                ["functionResponse"] = FunctionResponse(r),
            }).ToArray()),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };

    /// <summary>Gemini 3 assigns ids to function calls and expects them back; older models do not send one.</summary>
    private static JsonObject FunctionResponse(ToolResult r)
    {
        var node = new JsonObject
        {
            ["name"] = r.Name,
            ["response"] = new JsonObject { ["result"] = r.Content },
        };
        if (!r.CallId.StartsWith(SyntheticIdPrefix, StringComparison.Ordinal))
        {
            node["id"] = r.CallId;
        }

        return node;
    }

    private const string SyntheticIdPrefix = "call-";

    private static LlmResponse Parse(JsonElement root)
    {
        var candidate = root.GetProperty("candidates")[0];
        var content = candidate.GetProperty("content");
        var parts = content.TryGetProperty("parts", out var p) ? p : default;
        var text = string.Empty;
        var calls = new List<ToolCall>();
        var index = 0;
        if (parts.ValueKind == JsonValueKind.Array)
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out var t))
                {
                    text += t.GetString();
                }
                else if (part.TryGetProperty("functionCall", out var call))
                {
                    var name = call.GetProperty("name").GetString()!;
                    var id = call.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                        ? idElement.GetString()!
                        : $"{SyntheticIdPrefix}{index.ToString(CultureInfo.InvariantCulture)}-{name}";
                    index++;
                    var args = call.TryGetProperty("args", out var a) ? a.Clone() : JsonDocument.Parse("{}").RootElement;
                    calls.Add(new ToolCall(id, name, args));
                }
            }
        }

        var usage = root.GetProperty("usageMetadata");
        return new LlmResponse(
            new LlmMessage.AssistantTurn(content.Clone(), text, calls),
            calls.Count > 0 ? "tool_use" : "end_turn",
            usage.TryGetProperty("promptTokenCount", out var i) ? i.GetInt32() : 0,
            usage.TryGetProperty("candidatesTokenCount", out var o) ? o.GetInt32() : 0);
    }
}
