using System.Globalization;
using Orchestrator.Core.Contracts;
using Orchestrator.Core.Governance;
using Orchestrator.Core.State;
using Orchestrator.Core.Workflow;

namespace Orchestrator.Core.Engine;

/// <summary>
/// Runs one stage from entry gate to exit gate, including bounded retries, fallback agent and
/// human revision loops. Knows nothing about the graph; the scheduler decides *when* a stage
/// runs, the executor decides *whether it succeeded*.
/// </summary>
public sealed class Executor(
    IAgentRegistry agents,
    PolicyGate policies,
    ApprovalGate approvals,
    Saga saga,
    EventStore events,
    IReadOnlyDictionary<string, string> stageRoles)
{
    public const string FeedbackArtifact = "feedback";

    /// <summary>A human may send a stage back this many times before the run stops; independent of the retry budget.</summary>
    public const int MaxHumanRevisions = 3;

    public async Task<StageExecution> RunAsync(
        StageDefinition stage,
        Requirement requirement,
        IReadOnlyDictionary<string, Artifact> upstream,
        IReadOnlyList<Decision> decisions,
        IWorkspace workspace,
        CancellationToken ct)
    {
        events.Append(EventKind.StageStarted, stage.Id, ("agent", stage.Agent));

        var entry = CheckEntryGate(stage, upstream, decisions);
        if (!entry.Open)
        {
            return Fail(stage, $"entry gate closed: {entry.Summary}", []);
        }

        var stageCheckpoint = workspace.Checkpoint();
        var artifacts = new Dictionary<string, Artifact>(upstream, StringComparer.Ordinal);
        var newDecisions = new List<Decision>();
        var agent = agents.Resolve(stage.Agent);
        var usedFallback = false;
        var revisions = 0;

        for (var attempt = 1; ; attempt++)
        {
            if (ct.IsCancellationRequested)
            {
                return new StageExecution.Stopped(stage.Id);
            }

            var attemptCheckpoint = workspace.Checkpoint();
            var context = new StageContext(events.RunId, stage, requirement, artifacts, [.. decisions, .. newDecisions], workspace, attempt, ct);
            var attemptResult = await TryAttemptAsync(agent, context, attemptCheckpoint);
            if (attemptResult is AttemptOutcome.Cancelled)
            {
                await workspace.RestoreAsync(stageCheckpoint, CancellationToken.None);
                return new StageExecution.Stopped(stage.Id);
            }

            if (attemptResult is AttemptOutcome.Succeeded ok)
            {
                var exit = await CheckExitGateAsync(stage, ok.Result, artifacts, [.. decisions, .. newDecisions], workspace, attemptCheckpoint, ct);
                switch (exit)
                {
                    case ExitOutcome.Passed passed:
                        newDecisions.AddRange(passed.Decisions);
                        var produced = passed.Artifacts;
                        foreach (var artifact in produced)
                        {
                            events.Append(EventKind.ArtifactProduced, stage.Id,
                                ("name", artifact.Name), ("kind", artifact.Kind.ToString()), ("hash", artifact.ContentHash),
                                ("derivedFrom", string.Join(",", artifact.DerivedFrom)));
                        }

                        saga.Register(stage.Id, token => workspace.RestoreAsync(stageCheckpoint, token));
                        events.Append(EventKind.StageCompleted, stage.Id,
                            ("attempts", attempt.ToString(CultureInfo.InvariantCulture)), ("fallback", usedFallback.ToString()));
                        return new StageExecution.Completed(stage.Id, produced, newDecisions);

                    case ExitOutcome.Rejected rejected:
                        await workspace.RestoreAsync(stageCheckpoint, CancellationToken.None);
                        events.Append(EventKind.StageFailed, stage.Id, ("reason", "rejected by human"), ("rationale", rejected.Rationale));
                        return new StageExecution.Rejected(stage.Id, rejected.Rationale);

                    case ExitOutcome.Retry retry:
                        attemptResult = new AttemptOutcome.Failed(retry.Reason, retry.Feedback, retry.FromHuman);
                        break;
                }
            }

            var failed = (AttemptOutcome.Failed)attemptResult;
            events.Append(EventKind.StageAttemptFailed, stage.Id,
                ("attempt", attempt.ToString(CultureInfo.InvariantCulture)), ("reason", failed.Reason));
            await workspace.RestoreAsync(attemptCheckpoint, CancellationToken.None);
            artifacts[FeedbackArtifact] = failed.Feedback
                ?? new Artifact(FeedbackArtifact, ArtifactKind.Feedback, failed.Reason, stage.Id, []);

            if (failed.FromHuman && revisions < MaxHumanRevisions)
            {
                revisions++;
                continue; // a revision is not a failure; it does not consume the retry budget
            }

            if (RetryPolicy.CanRetry(stage.Retry, attempt))
            {
                var delay = RetryPolicy.DelayBefore(stage.Retry, attempt + 1);
                events.Append(EventKind.StageRetryScheduled, stage.Id,
                    ("nextAttempt", (attempt + 1).ToString(CultureInfo.InvariantCulture)), ("delayMs", delay.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture)));
                try
                {
                    await Task.Delay(delay, ct);
                }
                catch (OperationCanceledException)
                {
                    return new StageExecution.Stopped(stage.Id);
                }

                continue;
            }

            if (stage.FallbackAgent is { } fallbackRole && !usedFallback)
            {
                usedFallback = true;
                agent = agents.Resolve(fallbackRole);
                events.Append(EventKind.StageFallbackUsed, stage.Id, ("from", stage.Agent), ("to", fallbackRole));
                continue;
            }

            return Fail(stage, failed.Reason, [artifacts[FeedbackArtifact]]);
        }
    }

    private GateResult CheckEntryGate(StageDefinition stage, IReadOnlyDictionary<string, Artifact> upstream, IReadOnlyList<Decision> decisions)
    {
        var missing = stage.Entry.RequiredArtifacts.Where(name => !upstream.ContainsKey(name)).ToList();
        if (missing.Count > 0)
        {
            return new GateResult(false, [$"missing artifacts: {string.Join(", ", missing)}"]);
        }

        var context = new PolicyContext(stage, [], upstream, decisions, [], stageRoles);
        return policies.Evaluate(stage.Entry.Policies, context, "entry");
    }

    private async Task<ExitOutcome> CheckExitGateAsync(
        StageDefinition stage,
        StageResult result,
        IReadOnlyDictionary<string, Artifact> all,
        IReadOnlyList<Decision> decisions,
        IWorkspace workspace,
        WorkspaceCheckpoint since,
        CancellationToken ct)
    {
        var produced = result.Artifacts.ToDictionary(a => a.Name, a => a, StringComparer.Ordinal);
        var missing = stage.Exit.RequiredArtifacts.Where(name => !produced.ContainsKey(name)).ToList();
        if (missing.Count > 0)
        {
            return new ExitOutcome.Retry($"agent did not produce required artifact(s): {string.Join(", ", missing)}", null, FromHuman: false);
        }

        var changed = new List<ChangedFile>();
        foreach (var path in workspace.ChangedSince(since))
        {
            changed.Add(new ChangedFile(path, await workspace.ReadFileAsync(path, ct)));
        }

        var merged = new Dictionary<string, Artifact>(all, StringComparer.Ordinal);
        foreach (var artifact in result.Artifacts)
        {
            merged[artifact.Name] = artifact;
        }

        var gate = policies.Evaluate(stage.Exit.Policies, new PolicyContext(stage, result.Artifacts, merged, decisions, changed, stageRoles), "exit");
        if (!gate.Open)
        {
            var feedback = new Artifact(FeedbackArtifact, ArtifactKind.Feedback,
                "The previous attempt was blocked by policy. Fix the following and try again:\n- " + string.Join("\n- ", gate.Blocks),
                stage.Id, []);
            return new ExitOutcome.Retry($"exit gate closed: {gate.Summary}", feedback, FromHuman: false);
        }

        if (stage.Exit.Approval is null)
        {
            return new ExitOutcome.Passed(result.Artifacts, []);
        }

        var openAmbiguities = OpenAmbiguities(result.Artifacts);
        var approval = await approvals.RequestAsync(stage.Id, stage.Exit.Approval, result.Artifacts, openAmbiguities, ct);
        return approval.Kind switch
        {
            DecisionKind.Approved => new ExitOutcome.Passed(ApplyResolutions(result.Artifacts, approval.AmbiguityResolutions), approval.Decisions),
            DecisionKind.Rejected => new ExitOutcome.Rejected(approval.Rationale),
            _ => new ExitOutcome.Retry("revision requested by human",
                new Artifact(FeedbackArtifact, ArtifactKind.Feedback, $"A human reviewed your output and asked for changes:\n{approval.Rationale}", stage.Id, []),
                FromHuman: true),
        };
    }

    private static async Task<AttemptOutcome> TryAttemptAsync(IStageAgent agent, StageContext context, WorkspaceCheckpoint checkpoint)
    {
        try
        {
            var result = await agent.ExecuteAsync(context);
            return result.Outcome == StageOutcome.Succeeded
                ? new AttemptOutcome.Succeeded(result)
                : new AttemptOutcome.Failed(result.FailureReason ?? "agent reported failure",
                    result.Artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.Feedback), FromHuman: false);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            return new AttemptOutcome.Cancelled();
        }
        catch (Exception e)
        {
            return new AttemptOutcome.Failed($"{e.GetType().Name}: {e.Message}", null, FromHuman: false);
        }
    }

    private static List<Ambiguity> OpenAmbiguities(IReadOnlyList<Artifact> artifacts)
    {
        var spec = artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.Spec);
        return spec is not null && ArtifactJson.TryDeserialize<Spec>(spec.Content, out var parsed)
            ? parsed!.Ambiguities.Where(a => a.ResolvedOptionId is null).ToList()
            : [];
    }

    /// <summary>Writes the human's ambiguity choices back into the spec so downstream stages see them as settled.</summary>
    private static IReadOnlyList<Artifact> ApplyResolutions(IReadOnlyList<Artifact> artifacts, IReadOnlyDictionary<string, string> resolutions)
    {
        if (resolutions.Count == 0)
        {
            return artifacts;
        }

        return artifacts.Select(artifact =>
        {
            if (artifact.Kind != ArtifactKind.Spec || !ArtifactJson.TryDeserialize<Spec>(artifact.Content, out var spec))
            {
                return artifact;
            }

            var resolved = spec! with
            {
                Ambiguities = spec.Ambiguities
                    .Select(a => resolutions.TryGetValue(a.Id, out var option) ? a with { ResolvedOptionId = option } : a)
                    .ToList(),
            };
            return artifact with { Content = ArtifactJson.Serialize(resolved) };
        }).ToList();
    }

    private StageExecution.Failed Fail(StageDefinition stage, string reason, IReadOnlyList<Artifact> artifacts)
    {
        events.Append(EventKind.StageFailed, stage.Id, ("reason", reason));
        return new StageExecution.Failed(stage.Id, reason, artifacts);
    }

    private abstract record AttemptOutcome
    {
        public sealed record Succeeded(StageResult Result) : AttemptOutcome;
        public sealed record Failed(string Reason, Artifact? Feedback, bool FromHuman) : AttemptOutcome;
        public sealed record Cancelled : AttemptOutcome;
    }

    private abstract record ExitOutcome
    {
        public sealed record Passed(IReadOnlyList<Artifact> Artifacts, IReadOnlyList<Decision> Decisions) : ExitOutcome;
        public sealed record Rejected(string Rationale) : ExitOutcome;
        public sealed record Retry(string Reason, Artifact? Feedback, bool FromHuman) : ExitOutcome;
    }
}
