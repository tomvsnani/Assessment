using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orchestrator.Core.State;

/// <summary>
/// Append-only log for one run. Every event is written to <c>events.jsonl</c> before it is
/// visible in memory, so a crash can never leave the file behind the process's idea of the run.
/// Subscribers (audit log, console renderer) are notified synchronously after each append.
/// </summary>
public sealed class EventStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly List<RunEvent> _events = [];
    private readonly StreamWriter? _writer;
    private readonly Lock _gate = new();
    private long _seq;

    public EventStore(string runId, TimeProvider clock, string? filePath = null)
    {
        RunId = runId;
        Clock = clock;
        if (filePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
        }
    }

    public string RunId { get; }
    public TimeProvider Clock { get; }

    public event Action<RunEvent>? Appended;

    public IReadOnlyList<RunEvent> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    public RunEvent Append(EventKind kind, string? stageId = null, params (string Key, string Value)[] data)
    {
        RunEvent evt;
        lock (_gate)
        {
            evt = new RunEvent(++_seq, Clock.GetUtcNow(), RunId, kind, stageId,
                data.ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal));
            _writer?.WriteLine(JsonSerializer.Serialize(evt, JsonOptions));
            _events.Add(evt);
        }

        Appended?.Invoke(evt);
        return evt;
    }

    public static IReadOnlyList<RunEvent> ReadFile(string path) =>
        File.ReadLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<RunEvent>(line, JsonOptions)!)
            .ToList();

    public void Dispose() => _writer?.Dispose();
}
