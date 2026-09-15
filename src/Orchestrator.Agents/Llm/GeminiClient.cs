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
            // Gemini 2.5+ counts thinking tokens against this limit, so a 16k cap truncates long answers;
            // the model ceiling (64k) is the safe value and the loop handles MAX_TOKENS anyway.
            ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = Math.Max(request.MaxTokens, 65536) },
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

        return Parse(JsonDocument.Parse(json).RootElement, json);
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

    private LlmResponse Parse(JsonElement root, string json)
    {
        // A 200 is not a usable answer: Gemini returns no candidates when the prompt is blocked, and a
        // candidate without content (or with empty parts) on SAFETY/RECITATION/OTHER and sometimes on
        // MAX_TOKENS spent entirely on thinking. Either way there is nothing to hand the agent; treat it
        // as a transient provider failure so the tool loop retries the call instead of failing the stage.
        if (!root.TryGetProperty("candidates", out var candidates) || candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
        {
            throw new LlmException(Provider, 200, "no candidates in response: " + json, transient: true);
        }

        var candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Object)
        {
            throw new LlmException(Provider, 200, "candidate without content (finishReason " + FinishReason(candidate) + "): " + json, transient: true);
        }

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

        if (text.Length == 0 && calls.Count == 0)
        {
            throw new LlmException(Provider, 200, "candidate with no text and no function call (finishReason " + FinishReason(candidate) + "): " + json, transient: true);
        }

        var usage = root.TryGetProperty("usageMetadata", out var u) ? u : default;
        var finish = FinishReason(candidate);
        return new LlmResponse(
            new LlmMessage.AssistantTurn(content.Clone(), text, calls),
            calls.Count > 0 ? "tool_use" : finish == "MAX_TOKENS" ? "max_tokens" : "end_turn",
            usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("promptTokenCount", out var i) ? i.GetInt32() : 0,
            usage.ValueKind == JsonValueKind.Object && usage.TryGetProperty("candidatesTokenCount", out var o) ? o.GetInt32() : 0);
    }

    private static string? FinishReason(JsonElement candidate) =>
        candidate.TryGetProperty("finishReason", out var fr) && fr.ValueKind == JsonValueKind.String ? fr.GetString() : null;
}
