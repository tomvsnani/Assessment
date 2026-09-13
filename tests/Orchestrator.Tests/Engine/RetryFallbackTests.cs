using Orchestrator.Core.Contracts;
using Orchestrator.Core.Engine;
using Orchestrator.Core.Metrics;
using Orchestrator.Core.State;
using Orchestrator.Tests.Fakes;

namespace Orchestrator.Tests.Engine;

public class RetryFallbackTests
{
    private const string WithRetry = """
        name: retry
        requirement: r.md
        stages:
          - id: work
            agent: flaky
            retry: { max_attempts: 3 }
            fallback: steady
            exit: { artifacts: [result] }
        """;

    private static Artifact Result(StageContext ctx, string content = "done") => new("result", ArtifactKind.Code, content, ctx.Stage.Id, []);

    [Fact]
    public async Task Given_agent_fails_twice_When_run_Then_third_attempt_succeeds_and_feedback_carries_the_last_failure()
    {
        var attempts = new List<(int Attempt, string? Feedback)>();
        var agents = new FakeAgents()
            .Add("flaky", ctx =>
            {
                attempts.Add((ctx.Attempt, ctx.Artifacts.GetValueOrDefault(Executor.FeedbackArtifact)?.Content));
                return Task.FromResult(ctx.Attempt < 3 ? StageResult.Failure($"attempt {ctx.Attempt} broke") : StageResult.Success(Result(ctx)));
            })
            .Producing("steady", "result");
        using var run = new TestRun(WithRetry, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeTrue();
        attempts.Select(a => a.Attempt).Should().Equal(1, 2, 3);
        attempts[0].Feedback.Should().BeNull();
        attempts[1].Feedback.Should().Be("attempt 1 broke");
        attempts[2].Feedback.Should().Be("attempt 2 broke");
        run.EventsOfKind(EventKind.StageAttemptFailed).Should().HaveCount(2);
        run.EventsOfKind(EventKind.StageRetryScheduled).Should().HaveCount(2);
        run.EventsOfKind(EventKind.StageFallbackUsed).Should().BeEmpty();

        var metrics = ReliabilityMetrics.From(run.Events.All);
        metrics.Retries.Should().Be(2);
        metrics.SuccessRate.Should().Be(1.0);
        metrics.MeanTimeToRecover.Should().NotBeNull();
    }

    [Fact]
    public async Task Given_agent_always_fails_When_retries_exhausted_Then_fallback_agent_runs_once_and_succeeds()
    {
        var agents = new FakeAgents()
            .Add("flaky", _ => Task.FromResult(StageResult.Failure("never works")))
            .Add("steady", ctx => Task.FromResult(StageResult.Success(Result(ctx, "from fallback"))));
        using var run = new TestRun(WithRetry, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeTrue();
        run.EventsOfKind(EventKind.StageAttemptFailed).Should().HaveCount(3);
        var fallback = run.EventsOfKind(EventKind.StageFallbackUsed).Should().ContainSingle().Subject;
        fallback["from"].Should().Be("flaky");
        fallback["to"].Should().Be("steady");
        run.EventsOfKind(EventKind.ArtifactProduced).Single()["hash"].Should().Be(Artifact.Hash("from fallback"));
        ReliabilityMetrics.From(run.Events.All).Fallbacks.Should().Be(1);
    }

    [Fact]
    public async Task Given_fallback_also_fails_When_run_Then_stage_fails_and_run_stops()
    {
        var agents = new FakeAgents()
            .Add("flaky", _ => Task.FromResult(StageResult.Failure("never works")))
            .Add("steady", _ => Task.FromResult(StageResult.Failure("also broken")));
        using var run = new TestRun(WithRetry, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeFalse();
        run.EventsOfKind(EventKind.StageAttemptFailed).Should().HaveCount(4, "3 primary attempts + 1 fallback");
        run.EventsOfKind(EventKind.StageFailed).Single()["reason"].Should().Be("also broken");
    }

    [Fact]
    public async Task Given_agent_omits_required_artifact_When_run_Then_it_is_retried_with_feedback_naming_the_artifact()
    {
        string? feedback = null;
        var agents = new FakeAgents()
            .Add("flaky", ctx =>
            {
                feedback ??= ctx.Artifacts.GetValueOrDefault(Executor.FeedbackArtifact)?.Content;
                return Task.FromResult(ctx.Attempt == 1 ? StageResult.Success() : StageResult.Success(Result(ctx)));
            })
            .Producing("steady", "result");
        using var run = new TestRun(WithRetry, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeTrue();
        run.EventsOfKind(EventKind.StageAttemptFailed).Single()["reason"].Should().Contain("result");
    }
}
