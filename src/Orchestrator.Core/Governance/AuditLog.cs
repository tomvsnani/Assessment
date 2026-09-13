using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Orchestrator.Core.State;

namespace Orchestrator.Core.Governance;

/// <summary>
/// The compliance view of a run: who decided what, on which inputs, with what outcome.
/// One JSON object per line (same shape as the shortener's Serilog output, so both can be
/// shipped to the same log platform). Each entry carries the hash of the previous entry, so
/// removing or editing a line breaks the chain — tamper-evident, not merely append-only.
/// </summary>
public sealed class AuditLog : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly HashSet<EventKind> Audited =
    [
        EventKind.ApprovalRequested, EventKind.ApprovalDecided, EventKind.PolicyEvaluated,
        EventKind.ReplanTriggered, EventKind.CompensationRun, EventKind.RollbackCompleted,
        EventKind.SafeStopTriggered, EventKind.StageCompleted, EventKind.StageFailed,
        EventKind.ArtifactProduced, EventKind.RunCompleted, EventKind.RunFailed,
    ];

    private readonly EventStore _events;
    private readonly StreamWriter _writer;
    private readonly List<AuditEntry> _entries = [];
    private string _previousHash = "genesis";

    public AuditLog(EventStore events, string filePath)
    {
        _events = events;
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        _writer = new StreamWriter(filePath, append: true) { AutoFlush = true };
        _events.Appended += OnEvent;
    }

    public IReadOnlyList<AuditEntry> Entries => _entries;

    private void OnEvent(RunEvent evt)
    {
        if (!Audited.Contains(evt.Kind))
        {
            return;
        }

        var actor = evt.Kind is EventKind.ApprovalDecided ? evt["actor"] : "orchestrator";
        var outcome = evt.Kind switch
        {
            EventKind.PolicyEvaluated => evt["verdict"],
            EventKind.ApprovalDecided => evt["decision"],
            _ => evt.Kind.ToString(),
        };
        var detail = evt.Kind switch
        {
            EventKind.PolicyEvaluated => $"{evt["policy"]} ({evt["phase"]}): {evt["reason"]}",
            EventKind.ApprovalDecided => $"{evt["label"]}: {evt["rationale"]}",
            EventKind.ArtifactProduced => $"{evt["name"]}@{evt["hash"]}",
            _ => string.Join(" ", evt.Data.Select(kv => $"{kv.Key}={kv.Value}")),
        };

        var entry = new AuditEntry(evt.Seq, evt.At, evt.RunId, evt.StageId, actor, evt.Kind.ToString(), outcome, detail, _previousHash, string.Empty);
        entry = entry with { Hash = Hash(entry) };
        _previousHash = entry.Hash;
        _entries.Add(entry);
        _writer.WriteLine(JsonSerializer.Serialize(entry, JsonOptions));
    }

    private static string Hash(AuditEntry entry)
    {
        var payload = $"{entry.Seq}|{entry.At:O}|{entry.StageId}|{entry.Actor}|{entry.Action}|{entry.Outcome}|{entry.Detail}|{entry.PreviousHash}";
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))[..16];
    }

    /// <summary>Re-computes the chain; returns the sequence number of the first broken entry, or null when intact.</summary>
    public static long? Verify(IReadOnlyList<AuditEntry> entries)
    {
        var previous = "genesis";
        foreach (var entry in entries)
        {
            if (entry.PreviousHash != previous || Hash(entry) != entry.Hash)
            {
                return entry.Seq;
            }

            previous = entry.Hash;
        }

        return null;
    }

    public static IReadOnlyList<AuditEntry> ReadFile(string path) =>
        File.ReadLines(path).Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => JsonSerializer.Deserialize<AuditEntry>(l, JsonOptions)!).ToList();

    public void Dispose()
    {
        _events.Appended -= OnEvent;
        _writer.Dispose();
    }
}

public sealed record AuditEntry(
    long Seq,
    DateTimeOffset At,
    string RunId,
    string? StageId,
    string Actor,
    string Action,
    string Outcome,
    string Detail,
    string PreviousHash,
    string Hash);
