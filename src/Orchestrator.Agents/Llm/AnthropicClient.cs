using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Orchestrator.Agents.Llm;

/// <summary>
/// Raw HTTP adapter for the Anthropic Messages API (no SDK by design: see PLAN.md D3).
/// Thinking is left at the model default; thinking blocks come back inside the assistant
/// content and are echoed unchanged on the next turn, as the API requires.
/// </summary>
public sealed class AnthropicClient(HttpClient http, string apiKey, string model = AnthropicClient.DefaultModel) : ILlmClient
{
    public const string DefaultModel = "claude-opus-5";
    private const string Endpoint = "https://api.anthropic.com/v1/messages";

    public string Provider => "anthropic";

    public string Model => model;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["max_tokens"] = request.MaxTokens,
            ["system"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = request.System,
                ["cache_control"] = new JsonObject { ["type"] = "ephemeral" },
            }),
            ["messages"] = new JsonArray(request.Messages.Select(ToWire).ToArray()),
        };
        if (request.Tools.Count > 0)
        {
            body["tools"] = new JsonArray(request.Tools.Select(t => (JsonNode)new JsonObject
            {
                ["name"] = t.Name,
                ["description"] = t.Description,
                ["input_schema"] = JsonNode.Parse(t.InputSchema.GetRawText()),
            }).ToArray());
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint) { Content = JsonContent.Create(body) };
        message.Headers.Add("x-api-key", apiKey);
        message.Headers.Add("anthropic-version", "2023-06-01");

        using var response = await http.SendAsync(message, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new LlmException(Provider, (int)response.StatusCode, json);
        }

        return Parse(JsonDocument.Parse(json).RootElement);
    }

    private static JsonNode ToWire(LlmMessage message) => message switch
    {
        LlmMessage.UserText user => new JsonObject { ["role"] = "user", ["content"] = user.Text },
        LlmMessage.AssistantTurn assistant => new JsonObject { ["role"] = "assistant", ["content"] = JsonNode.Parse(assistant.RawContent.GetRawText()) },
        LlmMessage.ToolResults results => new JsonObject
        {
            ["role"] = "user",
            ["content"] = new JsonArray(results.Results.Select(r => (JsonNode)new JsonObject
            {
                ["type"] = "tool_result",
                ["tool_use_id"] = r.CallId,
                ["content"] = r.Content,
                ["is_error"] = r.IsError,
            }).ToArray()),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };

    private static LlmResponse Parse(JsonElement root)
    {
        var content = root.GetProperty("content");
        var text = string.Join("\n", content.EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "text")
            .Select(b => b.GetProperty("text").GetString()));
        var calls = content.EnumerateArray()
            .Where(b => b.GetProperty("type").GetString() == "tool_use")
            .Select(b => new ToolCall(b.GetProperty("id").GetString()!, b.GetProperty("name").GetString()!, b.GetProperty("input").Clone()))
            .ToList();
        var usage = root.GetProperty("usage");

        return new LlmResponse(
            new LlmMessage.AssistantTurn(content.Clone(), text, calls),
            root.GetProperty("stop_reason").GetString() ?? "end_turn",
            usage.GetProperty("input_tokens").GetInt32(),
            usage.GetProperty("output_tokens").GetInt32());
    }
}

/// <param name="transient">Overrides the status-based transience: a well-formed 200 with no usable
/// candidate (Gemini safety/recitation/empty responses) is retried like a 503.</param>
public sealed partial class LlmException(string provider, int status, string body, bool? transient = null)
    : Exception($"{provider} returned HTTP {status}: {Truncate(body)}")
{
    public int Status { get; } = status;

    public bool IsTransient => transient ?? Status is 429 or 500 or 502 or 503 or 529;

    /// <summary>Gemini puts a retryDelay ("34s") in the 429 body; other providers leave this null.</summary>
    public TimeSpan? RetryAfter { get; } = RetryDelay().Match(body) is { Success: true } m ? TimeSpan.FromSeconds(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)) : null;

    private static string Truncate(string s) => s.Length <= 500 ? s : s[..500] + "...";

    [System.Text.RegularExpressions.GeneratedRegex(@"""retryDelay""\s*:\s*""(\d+(?:\.\d+)?)s""")]
    private static partial System.Text.RegularExpressions.Regex RetryDelay();
}
