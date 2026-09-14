using System.Diagnostics;
using System.Globalization;
using Orchestrator.Agents.Llm;
using Orchestrator.Agents.Tools;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Agents;

/// <summary>
/// The one agentic loop every LLM-backed role shares: send the conversation, execute whatever
/// tools the model asked for (all of them, results returned in one message), repeat until the
/// model stops calling tools or the iteration budget is spent. Every turn and tool call is
/// reported through <see cref="IRunTrace"/> so the dashboard can show the agent working.
/// </summary>
public sealed class AgentToolLoop(ILlmClient client, IRunTrace trace, Action<string> log)
{
    public const int DefaultMaxIterations = 40;

    public sealed record Outcome(string FinalText, int Iterations, int ToolCalls, int InputTokens, int OutputTokens, bool HitIterationLimit);

    public async Task<Outcome> RunAsync(
        string stageId,
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
            trace.ModelCallStarted(stageId, label, iteration, messages.Count);
            var response = await CompleteWithRetryAsync(stageId, label, new LlmRequest(systemPrompt, [.. messages], definitions), ct);
            inputTokens += response.InputTokens;
            outputTokens += response.OutputTokens;
            messages.Add(response.Turn);
            trace.AgentTurn(stageId, label, iteration, response.Turn.Text, response.Turn.ToolCalls.Count, response.InputTokens, response.OutputTokens);

            if (!response.WantsTools)
            {
                return new Outcome(response.Turn.Text, iteration, toolCalls, inputTokens, outputTokens, false);
            }

            var results = new List<ToolResult>();
            foreach (var call in response.Turn.ToolCalls)
            {
                toolCalls++;
                results.Add(await InvokeAsync(stageId, label, toolsByName, call, ct));
            }

            messages.Add(new LlmMessage.ToolResults(results));
        }

        log($"{label}: iteration limit ({maxIterations}) reached; asking for the final answer");
        messages.Add(new LlmMessage.UserText("You have used your tool budget. Stop using tools and produce your final answer now."));
        trace.ModelCallStarted(stageId, label, maxIterations + 1, messages.Count);
        var last = await CompleteWithRetryAsync(stageId, label, new LlmRequest(systemPrompt, [.. messages], []), ct);
        trace.AgentTurn(stageId, label, maxIterations + 1, last.Turn.Text, 0, last.InputTokens, last.OutputTokens);
        return new Outcome(last.Turn.Text, maxIterations + 1, toolCalls, inputTokens + last.InputTokens, outputTokens + last.OutputTokens, true);
    }

    private async Task<ToolResult> InvokeAsync(string stageId, string label, Dictionary<string, ITool> tools, ToolCall call, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        trace.ToolStarted(stageId, label, call.Name, DescribeArguments(call));
        ToolResult result;
        if (!tools.TryGetValue(call.Name, out var tool))
        {
            result = new ToolResult(call.Id, call.Name, $"ERROR: unknown tool '{call.Name}'", IsError: true);
        }
        else
        {
            try
            {
                var output = await tool.InvokeAsync(call.Input, ct);
                result = new ToolResult(call.Id, call.Name, output, output.StartsWith("ERROR", StringComparison.Ordinal));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                result = new ToolResult(call.Id, call.Name, $"ERROR: {e.GetType().Name}: {e.Message}", IsError: true);
            }
        }

        trace.ToolInvoked(stageId, label, call.Name, DescribeArguments(call), result.Content.Truncate(600), result.IsError, stopwatch.Elapsed);
        log($"  tool {call.Name} {Describe(call)} -> {result.Content.Split('\n')[0].Truncate(80)}");
        return result;
    }

    /// <summary>
    /// Transient provider errors (429, 5xx) are retried with exponential backoff, honouring a
    /// provider-supplied retry delay when there is one (free-tier rate limits are the common case);
    /// anything else surfaces to the executor, whose own bounded retry then applies.
    /// </summary>
    private const int MaxProviderAttempts = 8;

    private async Task<LlmResponse> CompleteWithRetryAsync(string stageId, string label, LlmRequest request, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await client.CompleteAsync(request, ct);
            }
            catch (LlmException e) when (e.IsTransient && attempt < MaxProviderAttempts)
            {
                var delay = e.RetryAfter is { } hinted ? hinted + TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt) * 2));
                trace.ProviderRetry(stageId, label, e.Status, attempt, MaxProviderAttempts, delay, e.Message);
                log($"  provider {e.Status}; retrying in {delay.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)}s (attempt {attempt}/{MaxProviderAttempts})");
                await Task.Delay(delay, ct);
            }
            catch (HttpRequestException e) when (attempt < MaxProviderAttempts)
            {
                var delay = TimeSpan.FromSeconds(Math.Min(60, Math.Pow(2, attempt) * 2));
                trace.ProviderRetry(stageId, label, 0, attempt, MaxProviderAttempts, delay, e.Message);
                await Task.Delay(delay, ct);
            }
        }
    }

    /// <summary>write_file arguments carry whole files; the trace shows the path and size, the recording keeps the content.</summary>
    private static string DescribeArguments(ToolCall call) =>
        call.Name == "write_file"
            ? $"{{\"path\": \"{call.Input.OptionalArg("path")}\", \"content\": \"({call.Input.OptionalArg("content")?.Length ?? 0} chars)\"}}"
            : call.Input.GetRawText();

    private static string Describe(ToolCall call) =>
        call.Input.TryGetProperty("path", out var p) ? p.GetString() ?? string.Empty
        : call.Input.TryGetProperty("pattern", out var q) ? q.GetString() ?? string.Empty
        : string.Empty;
}

internal static class StringTruncation
{
    public static string Truncate(this string s, int max) => s.Length <= max ? s : s[..max] + "...";
}
