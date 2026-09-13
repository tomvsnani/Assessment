using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestrator.Agents.Llm;

/// <summary>
/// Record/replay around any <see cref="ILlmClient"/>.
/// <para>Record: every exchange is written to <c>recordings/&lt;scenario&gt;/llm/NNN-&lt;label&gt;-&lt;hash&gt;.json</c>.</para>
/// <para>Replay: a request is matched by the hash of its content. If nothing matches (tool output
/// drifted, e.g. build timings), the next unused recording with the same label is used and the
/// drift is reported — the run still replays, and the report says exactly where it stopped being exact.</para>
/// </summary>
public sealed class RecordingClient : ILlmClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(), new LlmMessageConverter() },
    };

    private readonly ILlmClient? _inner;
    private readonly string _directory;
    private readonly List<Recording> _available = [];
    private readonly Action<string> _report;
    private int _sequence;

    private RecordingClient(ILlmClient? inner, string directory, RecordingMode mode, Action<string> report)
    {
        _inner = inner;
        _directory = directory;
        _report = report;
        Mode = mode;
        Directory.CreateDirectory(directory);
        if (mode == RecordingMode.Replay)
        {
            _available.AddRange(Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal)
                .Select(f => JsonSerializer.Deserialize<Recording>(File.ReadAllText(f), Json)!));
            Provider = _available.FirstOrDefault()?.Provider ?? "replay";
            Model = _available.FirstOrDefault()?.Model ?? "replay";
        }
        else
        {
            Provider = inner!.Provider;
            Model = inner.Model;
        }
    }

    public static RecordingClient Record(ILlmClient inner, string directory, Action<string> report)
    {
        foreach (var stale in Directory.Exists(directory) ? Directory.GetFiles(directory, "*.json") : [])
        {
            File.Delete(stale); // a new live run replaces the previous recording wholesale
        }

        return new RecordingClient(inner, directory, RecordingMode.Record, report);
    }

    public static RecordingClient Replay(string directory, Action<string> report) =>
        new(null, directory, RecordingMode.Replay, report);

    public RecordingMode Mode { get; }

    public string Provider { get; }

    public string Model { get; }

    /// <summary>Set by the agent loop so recordings are named after the role that made the call.</summary>
    public string CurrentLabel { get; set; } = "unlabelled";

    public int ExactMatches { get; private set; }

    public int SequenceMatches { get; private set; }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        var hash = HashRequest(request);
        if (Mode == RecordingMode.Record)
        {
            var response = await _inner!.CompleteAsync(request, ct);
            var recording = new Recording(++_sequence, CurrentLabel, hash, _inner.Provider, _inner.Model, DateTimeOffset.UtcNow, request, response);
            var name = $"{_sequence:000}-{CurrentLabel}-{hash}.json";
            await File.WriteAllTextAsync(Path.Combine(_directory, name), JsonSerializer.Serialize(recording, Json), ct);
            return response;
        }

        var exact = _available.FirstOrDefault(r => r.RequestHash == hash);
        if (exact is not null)
        {
            _available.Remove(exact);
            ExactMatches++;
            return exact.Response;
        }

        var bySequence = _available.FirstOrDefault(r => r.Label == CurrentLabel);
        if (bySequence is null)
        {
            throw new InvalidOperationException(
                $"No recording left for '{CurrentLabel}' (request hash {hash}). Re-record with --live, or check that prompts/ and the workspace baseline are unchanged.");
        }

        _available.Remove(bySequence);
        SequenceMatches++;
        _report($"replay: no exact recording for {CurrentLabel} #{bySequence.Sequence}; using it by sequence (input drifted)");
        return bySequence.Response;
    }

    internal static string HashRequest(LlmRequest request)
    {
        var canonical = JsonSerializer.Serialize(new { request.System, request.Messages, Tools = request.Tools.Select(t => t.Name) }, Json);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..12];
    }

    /// <summary>One recorded exchange. Committed to the repository so graders can replay without a key.</summary>
    public sealed record Recording(
        int Sequence,
        string Label,
        string RequestHash,
        string Provider,
        string Model,
        DateTimeOffset RecordedAt,
        LlmRequest Request,
        LlmResponse Response);

    /// <summary>Serialises the LlmMessage hierarchy with a discriminator so it round-trips.</summary>
    private sealed class LlmMessageConverter : JsonConverter<LlmMessage>
    {
        public override LlmMessage? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;
            var kind = root.GetProperty("kind").GetString();
            return kind switch
            {
                "user" => new LlmMessage.UserText(root.GetProperty("text").GetString()!),
                "assistant" => new LlmMessage.AssistantTurn(
                    root.GetProperty("rawContent").Clone(),
                    root.GetProperty("text").GetString()!,
                    root.GetProperty("toolCalls").Deserialize<List<ToolCall>>(options)!),
                "toolResults" => new LlmMessage.ToolResults(root.GetProperty("results").Deserialize<List<ToolResult>>(options)!),
                _ => throw new JsonException($"Unknown message kind '{kind}'."),
            };
        }

        public override void Write(Utf8JsonWriter writer, LlmMessage value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            switch (value)
            {
                case LlmMessage.UserText user:
                    writer.WriteString("kind", "user");
                    writer.WriteString("text", user.Text);
                    break;
                case LlmMessage.AssistantTurn assistant:
                    writer.WriteString("kind", "assistant");
                    writer.WriteString("text", assistant.Text);
                    writer.WritePropertyName("toolCalls");
                    JsonSerializer.Serialize(writer, assistant.ToolCalls, options);
                    writer.WritePropertyName("rawContent");
                    assistant.RawContent.WriteTo(writer);
                    break;
                case LlmMessage.ToolResults results:
                    writer.WriteString("kind", "toolResults");
                    writer.WritePropertyName("results");
                    JsonSerializer.Serialize(writer, results.Results, options);
                    break;
            }

            writer.WriteEndObject();
        }
    }
}

public enum RecordingMode
{
    Record,
    Replay,
}
