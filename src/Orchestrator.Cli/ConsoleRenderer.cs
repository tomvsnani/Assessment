using System.Globalization;
using Orchestrator.Core.State;

namespace Orchestrator.Cli;

/// <summary>Prints the event stream as it happens, one readable line per event that a human cares about.</summary>
public sealed class ConsoleRenderer : IDisposable
{
    private readonly EventStore _events;
    private readonly DateTimeOffset _start;

    public ConsoleRenderer(EventStore events)
    {
        _events = events;
        _start = events.Clock.GetUtcNow();
        _events.Appended += OnEvent;
    }

    private void OnEvent(RunEvent evt)
    {
        var line = evt.Kind switch
        {
            EventKind.RunStarted => $"run started: workflow={evt["workflow"]} kind={evt["kind"]} requirement={evt["requirement"]}",
            EventKind.StageStarted => $"▶ {evt.StageId} ({evt["agent"]})",
            EventKind.StageCompleted => $"✔ {evt.StageId} completed after {evt["attempts"]} attempt(s){(evt["fallback"] == "True" ? " via fallback" : string.Empty)}",
            EventKind.StageAttemptFailed => $"✖ {evt.StageId} attempt {evt["attempt"]} failed: {evt["reason"]}",
            EventKind.StageRetryScheduled => $"↻ {evt.StageId} retry #{evt["nextAttempt"]} in {evt["delayMs"]} ms",
            EventKind.StageFallbackUsed => $"↷ {evt.StageId} falling back from {evt["from"]} to {evt["to"]}",
            EventKind.StageFailed => $"■ {evt.StageId} FAILED: {evt["reason"]}",
            EventKind.StageInvalidated => $"⟲ {evt.StageId} invalidated (because of {evt["because"]})",
            EventKind.ArtifactProduced => $"  artifact {evt["name"]}@{evt["hash"]}{(evt["derivedFrom"].Length > 0 ? " ← " + evt["derivedFrom"] : string.Empty)}",
            EventKind.PolicyEvaluated when evt["verdict"] != "Pass" => $"  policy {evt["policy"]} [{evt["phase"]}] {evt["verdict"].ToUpperInvariant()}: {evt["reason"]}",
            EventKind.PolicyEvaluated => $"  policy {evt["policy"]} [{evt["phase"]}] pass",
            EventKind.ApprovalRequested => $"  approval requested: {evt["label"]} ({evt["openAmbiguities"]} open ambiguities)",
            EventKind.ApprovalDecided => $"  decision {evt["decisionId"]}: {evt["decision"]} by {evt["actor"]} — {evt["rationale"]}",
            EventKind.ReplanTriggered => $"⇄ re-plan ({(evt["accepted"] == "true" ? "accepted" : "refused")}): {evt["cause"]}; invalidated [{evt["invalidated"]}]",
            EventKind.CompensationRun => $"  compensated {evt.StageId}: {evt["outcome"]}",
            EventKind.RollbackCompleted => $"⏪ rollback completed ({evt["stagesUndone"]} stage(s)): {evt["reason"]}",
            EventKind.SafeStopTriggered => $"⛔ safe stop: {evt["reason"]}",
            EventKind.RunCompleted => "run completed",
            EventKind.RunFailed => $"run failed: {evt["reason"]}",
            _ => null,
        };

        if (line is not null)
        {
            var elapsed = (evt.At - _start).TotalSeconds;
            Console.WriteLine($"[{elapsed,7:0.0}s] {line}");
        }
    }

    public void Dispose() => _events.Appended -= OnEvent;
}
