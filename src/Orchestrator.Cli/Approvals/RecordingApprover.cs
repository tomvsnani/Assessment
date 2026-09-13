using System.Text.Json;
using Orchestrator.Core.Governance;

namespace Orchestrator.Cli.Approvals;

/// <summary>Wraps a live approver and appends every decision to decisions.jsonl so a replay can reproduce the human.</summary>
public sealed class RecordingApprover(IApprover inner, string filePath) : IApprover
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private int _seq;

    public static RecordingApprover StartFresh(IApprover inner, string filePath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.Delete(filePath);
        return new RecordingApprover(inner, filePath);
    }

    public async Task<ApprovalDecision> DecideAsync(ApprovalRequest request, CancellationToken ct)
    {
        var decision = await inner.DecideAsync(request, ct);
        var record = new RecordedDecision(
            ++_seq, request.Label, request.StageId, decision.Actor, decision.Kind, decision.Rationale,
            decision.AmbiguityResolutions,
            string.Join(",", request.ArtifactsToReview.Select(a => $"{a.Name}@{a.ContentHash}")),
            DateTimeOffset.UtcNow);
        await File.AppendAllTextAsync(filePath, JsonSerializer.Serialize(record, Json) + Environment.NewLine, ct);
        return decision;
    }

    public static IReadOnlyList<RecordedDecision> Read(string filePath) =>
        File.Exists(filePath)
            ? File.ReadLines(filePath).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => JsonSerializer.Deserialize<RecordedDecision>(l, Json)!).ToList()
            : [];
}
