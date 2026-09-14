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
            // Shared read so dashboards and tail -f can follow the log while the run writes it.
            _writer = new StreamWriter(new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
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
        ReadSharedLines(path)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<RunEvent>(line, JsonOptions)!)
            .ToList();

    /// <summary>Reads a log that another process (or this one) may still be appending to.</summary>
    public static IEnumerable<string> ReadSharedLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    public void Dispose() => _writer?.Dispose();
}
