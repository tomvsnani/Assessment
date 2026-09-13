using System.Globalization;
using Orchestrator.Agents.Llm;
using Orchestrator.Agents.Tools;

namespace Orchestrator.Agents.Agents;

/// <summary>
/// The one agentic loop every LLM-backed role shares: send the conversation, execute whatever
/// tools the model asked for (all of them, results returned in one message), repeat until the
/// model stops calling tools or the iteration budget is spent.
/// </summary>
public sealed class AgentToolLoop(ILlmClient client, Action<string> log)
{
    public const int DefaultMaxIterations = 40;

    public sealed record Outcome(string FinalText, int Iterations, int ToolCalls, int InputTokens, int OutputTokens, bool HitIterationLimit);

    public async Task<Outcome> RunAsync(
        string label,
        string systemPrompt,
        string userMessage,
        IReadOnlyList<ITool> tools,
        CancellationToken ct,
        int maxIterations = DefaultMaxIterations)
    {
        if (client is RecordingClient recording)
        {
            recording.CurrentLabel = label;
        }

        var toolsByName = tools.ToDictionary(t => t.Name, t => t, StringComparer.Ordinal);
        var definitions = tools.Select(t => t.ToDefinition()).ToList();
        var messages = new List<LlmMessage> { new LlmMessage.UserText(userMessage) };
        var inputTokens = 0;
        var outputTokens = 0;
        var toolCalls = 0;

        for (var iteration = 1; iteration <= maxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();
            var response = await CompleteWithRetryAsync(new LlmRequest(systemPrompt, [.. messages], definitions), ct);
            inputTokens += response.InputTokens;
            outputTokens += response.OutputTokens;
            messages.Add(response.Turn);

            if (!response.WantsTools)
            {
                return new Outcome(response.Turn.Text, iteration, toolCalls, inputTokens, outputTokens, false);
            }

            var results = new List<ToolResult>();
            foreach (var call in response.Turn.ToolCalls)
            {
                toolCalls++;
                results.Add(await InvokeAsync(toolsByName, call, ct));
            }

            messages.Add(new LlmMessage.ToolResults(results));
        }

        log($"{label}: iteration limit ({maxIterations}) reached; asking for the final answer");
        messages.Add(new LlmMessage.UserText("You have used your tool budget. Stop using tools and produce your final answer now."));
        var last = await CompleteWithRetryAsync(new LlmRequest(systemPrompt, [.. messages], []), ct);
        return new Outcome(last.Turn.Text, maxIterations + 1, toolCalls, inputTokens + last.InputTokens, outputTokens + last.OutputTokens, true);
    }

    private async Task<ToolResult> InvokeAsync(Dictionary<string, ITool> tools, ToolCall call, CancellationToken ct)
    {
        if (!tools.TryGetValue(call.Name, out var tool))
        {
            return new ToolResult(call.Id, call.Name, $"ERROR: unknown tool '{call.Name}'", IsError: true);
        }

        try
        {
            var output = await tool.InvokeAsync(call.Input, ct);
            log($"  tool {call.Name} {Describe(call)} -> {output.Split('\n')[0].Truncate(80)}");
            return new ToolResult(call.Id, call.Name, output, output.StartsWith("ERROR", StringComparison.Ordinal));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            log($"  tool {call.Name} threw {e.GetType().Name}: {e.Message}");
            return new ToolResult(call.Id, call.Name, $"ERROR: {e.GetType().Name}: {e.Message}", IsError: true);
        }
    }

    /// <summary>Transient provider errors (429, 5xx) are retried a few times; anything else surfaces to the executor.</summary>
    private async Task<LlmResponse> CompleteWithRetryAsync(LlmRequest request, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await client.CompleteAsync(request, ct);
            }
            catch (LlmException e) when (e.IsTransient && attempt < 4)
            {
                var delay = TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2);
                log($"  provider {e.Status}; retrying in {delay.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)}s");
                await Task.Delay(delay, ct);
            }
            catch (HttpRequestException) when (attempt < 4)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt) * 2), ct);
            }
        }
    }

    private static string Describe(ToolCall call) =>
        call.Input.TryGetProperty("path", out var p) ? p.GetString() ?? string.Empty
        : call.Input.TryGetProperty("pattern", out var q) ? q.GetString() ?? string.Empty
        : string.Empty;
}

internal static class StringTruncation
{
    public static string Truncate(this string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
