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
    public async Task Given_attempt_writes_files_then_fails_When_retried_Then_next_attempt_sees_those_files_and_the_stage_lists_them_as_changed()
    {
        // The case seen live: a green build, then an empty final message. Redoing the build from scratch is the wrong response.
        var filesSeen = new List<IReadOnlyList<string>>();
        var agents = new FakeAgents()
            .Add("flaky", async ctx =>
            {
                filesSeen.Add(ctx.Workspace.ListFiles());
                await ctx.Workspace.WriteFileAsync($"src/Attempt{ctx.Attempt}.cs", "class C {}", ctx.CancellationToken);
                return ctx.Attempt == 1
                    ? StageResult.Malformed("final message had no artifact tag")
                    : StageResult.Success(Result(ctx, string.Join(",", ctx.Workspace.ChangedSince(ctx.StageStart))));
            })
            .Producing("steady", "result");
        using var run = new TestRun(WithRetry, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeTrue();
        filesSeen[1].Should().Equal("src/Attempt1.cs");
        run.EventsOfKind(EventKind.StageAttemptFailed).Single()["workspace"].Should().Be("kept");
        run.Workspace.Files.Keys.Should().BeEquivalentTo("src/Attempt1.cs", "src/Attempt2.cs");
        run.Events.All.Single(e => e.Kind == EventKind.ArtifactProduced)["name"].Should().Be("result");
        run.EventsOfKind(EventKind.StageCompleted).Should().ContainSingle();
    }

    [Fact]
    public async Task Given_every_attempt_writes_files_and_fails_When_stage_fails_Then_workspace_is_restored_to_the_stage_start()
    {
        var agents = new FakeAgents()
            .Add("flaky", async ctx =>
            {
                await ctx.Workspace.WriteFileAsync($"src/Attempt{ctx.Attempt}.cs", "class C {}", ctx.CancellationToken);
                return StageResult.Failure($"attempt {ctx.Attempt} broke");
            })
            .Add("steady", async ctx =>
            {
                await ctx.Workspace.WriteFileAsync("src/Fallback.cs", "class F {}", ctx.CancellationToken);
                return StageResult.Failure("fallback broke too");
            });
        using var run = new TestRun(WithRetry, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeFalse();
        run.Workspace.Files.Should().BeEmpty("a stage that fails outright must not leave its attempts' files behind");
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
