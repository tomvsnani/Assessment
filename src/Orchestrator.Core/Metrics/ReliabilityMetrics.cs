using System.Globalization;
using System.Text;
using Orchestrator.Core.State;

namespace Orchestrator.Core.Metrics;

/// <summary>
/// The reliability numbers the assessment asks for, computed from the event log so they are the
/// same whether the run was live or replayed.
/// <list type="bullet">
/// <item>Success rate: stages completed / stages attempted (a stage that was retried counts once).</item>
/// <item>Retry / fallback / rollback counts.</item>
/// <item>MTTR: mean time from a stage's first failed attempt to its eventual completion.</item>
/// <item>Latency: per stage (first start to final completion) and end to end.</item>
/// </list>
/// </summary>
public sealed record ReliabilityMetrics(
    int StagesAttempted,
    int StagesSucceeded,
    int Retries,
    int Fallbacks,
    int Replans,
    int Rollbacks,
    int PolicyBlocks,
    int HumanRejections,
    TimeSpan? MeanTimeToRecover,
    TimeSpan EndToEnd,
    IReadOnlyDictionary<string, TimeSpan> StageLatency,
    bool RunSucceeded)
{
    public double SuccessRate => StagesAttempted == 0 ? 0 : (double)StagesSucceeded / StagesAttempted;

    public static ReliabilityMetrics From(IReadOnlyList<RunEvent> events)
    {
        var started = events.FirstOrDefault(e => e.Kind == EventKind.RunStarted)?.At;
        var finished = events.LastOrDefault(e => e.Kind is EventKind.RunCompleted or EventKind.RunFailed)?.At;
        var endToEnd = started is { } s && finished is { } f ? f - s : TimeSpan.Zero;

        var attempted = events.Where(e => e.Kind == EventKind.StageStarted).Select(e => e.StageId!).Distinct(StringComparer.Ordinal).ToList();
        var succeeded = events.Where(e => e.Kind == EventKind.StageCompleted).Select(e => e.StageId!).Distinct(StringComparer.Ordinal)
            .Where(id => events.Last(e => e.StageId == id && e.Kind is EventKind.StageCompleted or EventKind.StageFailed or EventKind.StageInvalidated).Kind == EventKind.StageCompleted)
            .ToList();

        var recoveries = new List<TimeSpan>();
        foreach (var stageId in attempted)
        {
            var firstFailure = events.FirstOrDefault(e => e.StageId == stageId && e.Kind == EventKind.StageAttemptFailed);
            var completion = events.LastOrDefault(e => e.StageId == stageId && e.Kind == EventKind.StageCompleted);
            if (firstFailure is not null && completion is not null && completion.At > firstFailure.At)
            {
                recoveries.Add(completion.At - firstFailure.At);
            }
        }

        var stageLatency = attempted.ToDictionary(
            id => id,
            id =>
            {
                var first = events.First(e => e.StageId == id && e.Kind == EventKind.StageStarted).At;
                var last = events.LastOrDefault(e => e.StageId == id && e.Kind is EventKind.StageCompleted or EventKind.StageFailed)?.At ?? first;
                return last - first;
            },
            StringComparer.Ordinal);

        return new ReliabilityMetrics(
            StagesAttempted: attempted.Count,
            StagesSucceeded: succeeded.Count,
            Retries: events.Count(e => e.Kind == EventKind.StageRetryScheduled),
            Fallbacks: events.Count(e => e.Kind == EventKind.StageFallbackUsed),
            Replans: events.Count(e => e.Kind == EventKind.ReplanTriggered),
            Rollbacks: events.Count(e => e.Kind == EventKind.RollbackCompleted),
            PolicyBlocks: events.Count(e => e.Kind == EventKind.PolicyEvaluated && e["verdict"] == "Block"),
            HumanRejections: events.Count(e => e.Kind == EventKind.ApprovalDecided && e["decision"] != "Approved"),
            MeanTimeToRecover: recoveries.Count == 0 ? null : TimeSpan.FromTicks((long)recoveries.Average(r => r.Ticks)),
            EndToEnd: endToEnd,
            StageLatency: stageLatency,
            RunSucceeded: events.Any(e => e.Kind == EventKind.RunCompleted));
    }

    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine("reliability metrics");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  run outcome     : {(RunSucceeded ? "succeeded" : "failed / stopped")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  stage success   : {StagesSucceeded}/{StagesAttempted} ({SuccessRate:P0})");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  retries         : {Retries}   fallbacks: {Fallbacks}   re-plans: {Replans}   rollbacks: {Rollbacks}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  policy blocks   : {PolicyBlocks}   human rejections: {HumanRejections}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  MTTR            : {(MeanTimeToRecover is { } m ? m.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s" : "n/a (nothing failed)")}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"  end-to-end      : {EndToEnd.TotalSeconds:0.0} s");
        foreach (var (stage, latency) in StageLatency)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"    {stage,-20} {latency.TotalSeconds,8:0.0} s");
        }

        return sb.ToString();
    }
}
