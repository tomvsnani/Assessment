using Orchestrator.Core.Contracts;
using Orchestrator.Core.Engine;
using Orchestrator.Core.State;
using Orchestrator.Tests.Fakes;

namespace Orchestrator.Tests.Engine;

/// <summary>Human approvals and policies as they behave inside a full run.</summary>
public class GovernanceFlowTests
{
    private const string Gated = """
        name: gated
        stages:
          - id: requirements
            agent: requirements
            retry: { max_attempts: 2 }
            exit: { artifacts: [spec], policies: [no-secrets], approval: approve-spec }
          - id: implement
            agent: implementer
            depends_on: [requirements]
            exit: { artifacts: [implementation] }
        """;

    private static Spec SampleSpec(string? resolved = null) => new(
        "Retention", "Keep analytics compliant", ["retention"], ["billing"],
        [new AcceptanceCriterion("AC-1", "clicks older than the window", "the purge runs", "they are gone")],
        [new Ambiguity("AMB-1", "How long do we keep raw clicks?",
            [new AmbiguityOption("A", "30 days", "cheap, little history"), new AmbiguityOption("B", "13 months", "year-over-year, more storage")],
            "A", resolved)],
        ["one region"]);

    private static FakeAgents Agents(Func<StageContext, string> specContent) => new FakeAgents()
        .Add("requirements", ctx => Task.FromResult(StageResult.Success(new Artifact("spec", ArtifactKind.Spec, specContent(ctx), ctx.Stage.Id, []))))
        .Producing("implementer", "implementation", ArtifactKind.Code);

    [Fact]
    public async Task Given_human_rejects_spec_When_run_Then_run_stops_before_implementation_and_rejection_is_recorded()
    {
        var approver = new ScriptedApprover(ScriptedApprover.Reject("wrong product"));
        using var run = new TestRun(Gated, Agents(_ => ArtifactJson.Serialize(SampleSpec())), approver);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeFalse();
        outcome.Summary.Should().Contain("rejected by human").And.Contain("wrong product");
        run.StageOrder(EventKind.StageStarted).Should().NotContain("implement");
        var decided = run.EventsOfKind(EventKind.ApprovalDecided).Should().ContainSingle().Subject;
        decided["decision"].Should().Be("Rejected");
        decided["actor"].Should().Be("test-approver");
        run.EventsOfKind(EventKind.RollbackCompleted).Should().ContainSingle();
    }

    [Fact]
    public async Task Given_human_requests_revision_When_run_Then_stage_reruns_with_the_feedback_and_then_proceeds()
    {
        var feedbackSeen = new List<string?>();
        var approver = new ScriptedApprover(ScriptedApprover.Revise("add an assumption about regions"));
        var agents = Agents(ctx =>
        {
            feedbackSeen.Add(ctx.Artifacts.GetValueOrDefault(Executor.FeedbackArtifact)?.Content);
            return ArtifactJson.Serialize(SampleSpec());
        });
        using var run = new TestRun(Gated, agents, approver);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeTrue();
        feedbackSeen.Should().HaveCount(2);
        feedbackSeen[0].Should().BeNull();
        feedbackSeen[1].Should().Contain("add an assumption about regions");
        approver.Requests.Should().HaveCount(2);
        run.EventsOfKind(EventKind.ApprovalDecided).Select(e => e["decision"]).Should().Equal("RevisionRequested", "Approved");
        run.EventsOfKind(EventKind.StageRetryScheduled).Should().BeEmpty("a revision must not consume the retry budget");
    }

    [Fact]
    public async Task Given_open_ambiguity_When_human_resolves_it_at_approval_Then_downstream_sees_resolved_spec_and_decision()
    {
        Spec? seenByImplementer = null;
        IReadOnlyList<Decision>? decisionsSeen = null;
        var approver = new ScriptedApprover(ScriptedApprover.ApproveResolving("AMB-1", "B"));
        var agents = new FakeAgents()
            .Add("requirements", ctx => Task.FromResult(StageResult.Success(new Artifact("spec", ArtifactKind.Spec, ArtifactJson.Serialize(SampleSpec()), ctx.Stage.Id, []))))
            .Add("implementer", ctx =>
            {
                seenByImplementer = ArtifactJson.Deserialize<Spec>(ctx.Require("spec").Content);
                decisionsSeen = ctx.Decisions;
                return Task.FromResult(StageResult.Success(new Artifact("implementation", ArtifactKind.Code, "x", ctx.Stage.Id, ["spec"])));
            });
        using var run = new TestRun(Gated, agents, approver);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeTrue();
        approver.Requests.Single().OpenAmbiguities.Should().ContainSingle().Which.Id.Should().Be("AMB-1");
        seenByImplementer!.Ambiguities.Single().ResolvedOptionId.Should().Be("B");
        decisionsSeen.Should().Contain(d => d.Kind == DecisionKind.OptionChosen && d.Rationale == "AMB-1 -> B");
        decisionsSeen.Should().Contain(d => d.Kind == DecisionKind.Approved && d.CitedArtifacts.Contains("spec"));
    }

    [Fact]
    public async Task Given_agent_output_contains_a_secret_When_run_Then_policy_blocks_and_retry_gets_the_reason_as_feedback()
    {
        string? feedback = null;
        var agents = Agents(ctx =>
        {
            feedback ??= ctx.Artifacts.GetValueOrDefault(Executor.FeedbackArtifact)?.Content;
            return ctx.Attempt == 1
                ? "connect with ANTHROPIC_API_KEY=sk-ant-api03-ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789"
                : ArtifactJson.Serialize(SampleSpec());
        });
        using var run = new TestRun(Gated, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeTrue();
        var block = run.EventsOfKind(EventKind.PolicyEvaluated).First(e => e["verdict"] == "Block");
        block["policy"].Should().Be("no-secrets");
        block["reason"].Should().Contain("Anthropic API key");
        feedback.Should().Contain("blocked by policy").And.Contain("Anthropic API key");
        run.EventsOfKind(EventKind.ApprovalRequested).Should().ContainSingle("the human is only asked once the policy passes");
    }

    [Fact]
    public async Task Given_policy_keeps_blocking_When_retries_exhausted_Then_run_fails_with_the_policy_reason()
    {
        var agents = Agents(_ => "AKIAABCDEFGHIJKLMNOP is the key");
        using var run = new TestRun(Gated, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeFalse();
        outcome.Summary.Should().Contain("no-secrets").And.Contain("AWS access key");
        run.EventsOfKind(EventKind.ApprovalRequested).Should().BeEmpty();
    }
}

public class SafeStopDuringApprovalTests
{
    private const string Gated = """
        name: gated
        stages:
          - id: requirements
            agent: requirements
            exit: { artifacts: [spec], approval: approve-spec }
          - id: implement
            agent: implementer
            depends_on: [requirements]
        """;

    /// <summary>Blocks like a human who has not answered yet; honours cancellation like the web approver does.</summary>
    private sealed class WaitingApprover : Orchestrator.Core.Governance.IApprover
    {
        public TaskCompletionSource Asked { get; } = new();

        public async Task<Orchestrator.Core.Governance.ApprovalDecision> DecideAsync(Orchestrator.Core.Governance.ApprovalRequest request, CancellationToken ct)
        {
            Asked.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("unreachable");
        }
    }

    [Fact]
    public async Task Given_approval_pending_When_safe_stop_triggered_Then_run_ends_cleanly_with_rollback_and_no_exception()
    {
        var approver = new WaitingApprover();
        var agents = new FakeAgents()
            .Producing("requirements", "spec", ArtifactKind.Spec, "{}") // an empty spec must not break the gate
            .Producing("implementer", "implementation");
        using var run = new TestRun(Gated, agents, approver);

        var running = run.RunAsync();
        await approver.Asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        run.SafeStop.Trigger("operator stopped the run from the dashboard");
        var outcome = await running;

        outcome.Succeeded.Should().BeFalse();
        outcome.Summary.Should().Contain("dashboard");
        run.EventsOfKind(EventKind.RunFailed).Should().ContainSingle();
        run.EventsOfKind(EventKind.RollbackCompleted).Should().ContainSingle();
        run.EventsOfKind(EventKind.StageCompleted).Should().BeEmpty();
    }
}
