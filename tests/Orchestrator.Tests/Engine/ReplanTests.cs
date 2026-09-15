using Orchestrator.Core.Contracts;
using Orchestrator.Core.Engine;
using Orchestrator.Core.Metrics;
using Orchestrator.Core.State;
using Orchestrator.Core.Workflow;
using Orchestrator.Tests.Fakes;

namespace Orchestrator.Tests.Engine;

/// <summary>Non-linear execution: loops back to an earlier stage, and invalidation when upstream outputs change.</summary>
public class ReplanTests
{
    // implement -> {verify, docs}; verify failing sends the run back to implement (at most twice).
    private const string Loop = """
        name: loop
        stages:
          - { id: plan, agent: planner, exit: { artifacts: [plan] } }
          - { id: implement, agent: implementer, depends_on: [plan], exit: { artifacts: [implementation] } }
          - { id: docs, agent: writer, depends_on: [implement] }
          - id: verify
            agent: verifier
            depends_on: [implement]
            on_failure: { rerun_from: implement, max_loops: 2 }
          - { id: release, agent: releaser, depends_on: [verify, docs] }
        """;

    [Fact]
    public async Task Given_verify_fails_once_When_run_Then_implement_reruns_with_test_feedback_and_docs_are_rebuilt()
    {
        var implementFeedback = new List<string?>();
        var docsRuns = 0;
        var agents = new FakeAgents()
            .Producing("planner", "plan", ArtifactKind.Plan)
            .Add("implementer", ctx =>
            {
                implementFeedback.Add(ctx.Artifacts.GetValueOrDefault(Executor.FeedbackArtifact)?.Content);
                return Task.FromResult(StageResult.Success(new Artifact("implementation", ArtifactKind.Code, $"v{implementFeedback.Count}", ctx.Stage.Id, ["plan"])));
            })
            .Add("writer", _ => { docsRuns++; return Task.FromResult(StageResult.Success()); })
            .Add("verifier", ctx => Task.FromResult(ctx.Require("implementation").Content == "v1"
                ? StageResult.Failure("2 tests failed", new Artifact("test-report", ArtifactKind.Feedback, "FAIL: Redirect_returns_302", ctx.Stage.Id, ["implementation"]))
                : StageResult.Success(new Artifact("test-report", ArtifactKind.TestReport, "all green", ctx.Stage.Id, ["implementation"]))))
            .Producing("releaser", "change-record", ArtifactKind.ChangeRecord);
        using var run = new TestRun(Loop, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeTrue();
        implementFeedback.Should().HaveCount(2);
        implementFeedback[1].Should().Contain("FAIL: Redirect_returns_302");
        docsRuns.Should().Be(2, "docs were built on the first implementation and must be rebuilt after the re-plan");

        var replan = run.EventsOfKind(EventKind.ReplanTriggered).Should().ContainSingle().Subject;
        replan["rerunFrom"].Should().Be("implement");
        replan["loop"].Should().Be("1/2");
        replan["invalidated"].Split(',').Should().Contain("implement");
        run.EventsOfKind(EventKind.StageInvalidated).Select(e => e.StageId).Should().Contain("implement");
        run.EventsOfKind(EventKind.CompensationRun).Select(e => e.StageId).Should().Contain("docs", "stages built on the invalidated implementation are compensated")
            .And.NotContain("implement", "in fix mode the implementer keeps its files");
        ReliabilityMetrics.From(run.Events.All).Replans.Should().Be(1);
    }

    private const string ModeLoop = """
        name: loop
        stages:
          - { id: implement, agent: implementer, exit: { artifacts: [implementation] } }
          - { id: verify, agent: verifier, depends_on: [implement], on_failure: { rerun_from: implement, max_loops: 1, mode: MODE } }
        """;

    /// <summary>Implementer writes one file per attempt; the verifier fails the first attempt. Returns the files each attempt saw on entry.</summary>
    private static async Task<(RunOutcome Outcome, List<IReadOnlyList<string>> FilesSeen, TestRun Run)> RunModeLoopAsync(string mode)
    {
        var filesSeen = new List<IReadOnlyList<string>>();
        var attempts = 0;
        var agents = new FakeAgents()
            .Add("implementer", async ctx =>
            {
                attempts++;
                filesSeen.Add(ctx.Workspace.ListFiles());
                await ctx.Workspace.WriteFileAsync($"src/Attempt{attempts}.cs", "class C {}", ctx.CancellationToken);
                return StageResult.Success(new Artifact("implementation", ArtifactKind.Code, $"v{attempts}", ctx.Stage.Id, []));
            })
            .Add("verifier", ctx => Task.FromResult(ctx.Require("implementation").Content == "v1"
                ? StageResult.Failure("build failed", new Artifact("feedback", ArtifactKind.Feedback, "error CS1002 in src/Attempt1.cs", ctx.Stage.Id, []))
                : StageResult.Success()));
        var run = new TestRun(ModeLoop.Replace("MODE", mode, StringComparison.Ordinal), agents);
        var outcome = await run.RunAsync();
        return (outcome, filesSeen, run);
    }

    [Fact]
    public async Task Given_fix_mode_When_verify_fails_Then_implement_reruns_with_its_previous_files_still_present()
    {
        var (outcome, filesSeen, run) = await RunModeLoopAsync("fix");
        using (run)
        {
            outcome.Succeeded.Should().BeTrue();
            filesSeen.Should().HaveCount(2);
            filesSeen[0].Should().BeEmpty();
            filesSeen[1].Should().ContainSingle().Which.Should().Be("src/Attempt1.cs", "the failing attempt is kept so the feedback refers to code the implementer can read");
            run.Workspace.Files.Keys.Should().BeEquivalentTo("src/Attempt1.cs", "src/Attempt2.cs");

            var invalidated = run.EventsOfKind(EventKind.StageInvalidated).Single(e => e.StageId == "implement");
            invalidated["workspace"].Should().Be("kept");
            run.EventsOfKind(EventKind.ReplanTriggered).Single()["mode"].Should().Be("fix");
            run.EventsOfKind(EventKind.CompensationRun).Should().BeEmpty("nothing was undone");
        }
    }

    [Fact]
    public async Task Given_rollback_mode_When_verify_fails_Then_implement_reruns_from_a_clean_checkpoint()
    {
        var (outcome, filesSeen, run) = await RunModeLoopAsync("rollback");
        using (run)
        {
            outcome.Succeeded.Should().BeTrue();
            filesSeen.Should().HaveCount(2);
            filesSeen[1].Should().BeEmpty("rollback mode restores the checkpoint taken before implement ran");
            run.Workspace.Files.Keys.Should().BeEquivalentTo("src/Attempt2.cs");

            run.EventsOfKind(EventKind.StageInvalidated).Single(e => e.StageId == "implement")["workspace"].Should().Be("restored");
            run.EventsOfKind(EventKind.CompensationRun).Select(e => e.StageId).Should().Equal("implement");
        }
    }

    [Fact]
    public async Task Given_fix_mode_When_loop_budget_is_exhausted_Then_safe_stop_rollback_still_unwinds_every_attempt()
    {
        // The kept saga step is what makes this work: fix mode defers the compensation, it does not forget it.
        var attempts = 0;
        var agents = new FakeAgents()
            .Add("implementer", async ctx =>
            {
                attempts++;
                await ctx.Workspace.WriteFileAsync($"src/Attempt{attempts}.cs", "class C {}", ctx.CancellationToken);
                return StageResult.Success(new Artifact("implementation", ArtifactKind.Code, $"v{attempts}", ctx.Stage.Id, []));
            })
            .Add("verifier", _ => Task.FromResult(StageResult.Failure("still failing")));
        using var run = new TestRun(ModeLoop.Replace("MODE", "fix", StringComparison.Ordinal), agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeFalse();
        attempts.Should().Be(2, "initial + 1 loop");
        run.Workspace.Files.Should().BeEmpty("the run failed, so nothing the agents wrote may remain");
        run.EventsOfKind(EventKind.RollbackCompleted).Single()["stagesUndone"].Should().Be("2", "one compensation per completed implement attempt");
    }

    [Fact]
    public async Task Given_verify_keeps_failing_When_loop_budget_exhausted_Then_run_stops_rather_than_looping_forever()
    {
        var implementRuns = 0;
        var agents = new FakeAgents()
            .Producing("planner", "plan", ArtifactKind.Plan)
            .Add("implementer", ctx => { implementRuns++; return Task.FromResult(StageResult.Success(new Artifact("implementation", ArtifactKind.Code, $"v{implementRuns}", ctx.Stage.Id, []))); })
            .Producing("writer", "docs")
            .Add("verifier", _ => Task.FromResult(StageResult.Failure("still failing")))
            .Producing("releaser", "change-record");
        using var run = new TestRun(Loop, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeFalse();
        implementRuns.Should().Be(3, "initial + 2 loops");
        run.EventsOfKind(EventKind.ReplanTriggered).Last()["accepted"].Should().Be("false");
        run.EventsOfKind(EventKind.RollbackCompleted).Should().ContainSingle();
        run.StageOrder(EventKind.StageStarted).Should().NotContain("release");
    }

    [Fact]
    public async Task Given_completed_downstream_stages_When_upstream_artifact_changes_Then_coordinator_invalidates_them()
    {
        // Direct unit test of the second re-plan trigger, without needing a workflow that naturally produces it.
        var stages = new[]
        {
            new StageDefinition("a", "x", [], GateDefinition.Open, GateDefinition.Open, RetryDefinition.None, null, FailureHandling.StopRun),
            new StageDefinition("b", "x", ["a"], GateDefinition.Open, GateDefinition.Open, RetryDefinition.None, null, FailureHandling.StopRun),
            new StageDefinition("c", "x", ["b"], GateDefinition.Open, GateDefinition.Open, RetryDefinition.None, null, FailureHandling.StopRun),
        };
        using var events = new EventStore("run-x", TimeProvider.System);
        var state = new RunState();
        var saga = new Saga(events);
        var coordinator = new Coordinator(new DependencyGraph(stages), state, saga, events);
        var specV1 = new Artifact("spec", ArtifactKind.Spec, "v1", "a", []);
        state.MarkCompleted("a", [specV1], []);
        state.MarkCompleted("b", [new Artifact("design", ArtifactKind.Design, "d", "b", ["spec"])], []);
        state.MarkCompleted("c", [new Artifact("plan", ArtifactKind.Plan, "p", "c", ["design"])], []);
        var compensated = new List<string>();
        saga.Register("b", _ => { compensated.Add("b"); return Task.CompletedTask; });
        saga.Register("c", _ => { compensated.Add("c"); return Task.CompletedTask; });

        var before = state.ArtifactSnapshot();
        var specV2 = specV1 with { Content = "v2" };
        state.MarkCompleted("a", [specV2], []);
        var invalidated = await coordinator.ReactToArtifactsAsync("a", before, [specV2], CancellationToken.None);

        invalidated.Should().BeEquivalentTo("b", "c");
        compensated.Should().Equal("c", "b");
        state.Completed.Should().BeEquivalentTo("a");
        state.Artifact("design").Should().BeNull();
        state.Artifact("plan").Should().BeNull();
        events.All.Single(e => e.Kind == EventKind.ReplanTriggered)["cause"].Should().Contain("spec");
    }
}

public class ReplanFeedbackTests
{
    private const string Loop = """
        name: loop
        stages:
          - { id: implement, agent: implementer, exit: { artifacts: [implementation] } }
          - { id: verify, agent: verifier, depends_on: [implement], on_failure: { rerun_from: implement, max_loops: 2 } }
          - { id: review, agent: reviewer, depends_on: [verify], retry: { max_attempts: 1 }, on_failure: { rerun_from: implement, max_loops: 1 } }
        """;

    private static Artifact Feedback(StageContext ctx, string text) => new("feedback", ArtifactKind.Feedback, text, ctx.Stage.Id, []);

    [Fact]
    public async Task Given_review_findings_then_a_verify_failure_When_implement_reruns_Then_it_sees_both_pieces_of_feedback()
    {
        var seen = new List<string?>();
        var implementRuns = 0;
        var agents = new FakeAgents()
            .Add("implementer", ctx =>
            {
                implementRuns++;
                seen.Add(ctx.Artifacts.GetValueOrDefault(Executor.FeedbackArtifact)?.Content);
                return Task.FromResult(StageResult.Success(new Artifact("implementation", ArtifactKind.Code, $"v{implementRuns}", ctx.Stage.Id, [])));
            })
            .Add("verifier", ctx => Task.FromResult(ctx.Require("implementation").Content == "v2"
                ? StageResult.Failure("build failed", Feedback(ctx, "error CS0103: name does not exist"))
                : StageResult.Success()))
            .Add("reviewer", ctx => Task.FromResult(ctx.Require("implementation").Content == "v1"
                ? StageResult.Failure("reviewer requested changes", Feedback(ctx, "Blocking: race condition in IncrementUsageCount"))
                : StageResult.Success(new Artifact("review", ArtifactKind.Review, "VERDICT: APPROVE", ctx.Stage.Id, []))));
        using var run = new TestRun(Loop, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeTrue();
        seen.Should().HaveCount(3);
        seen[1].Should().Contain("race condition");
        seen[2].Should().Contain("race condition", "the reviewer's findings must survive the later build failure")
            .And.Contain("CS0103");
    }

    [Fact]
    public async Task Given_reviewer_output_is_malformed_When_retries_exhausted_Then_run_fails_without_looping_back_to_implement()
    {
        var implementRuns = 0;
        var agents = new FakeAgents()
            .Add("implementer", ctx => { implementRuns++; return Task.FromResult(StageResult.Success(new Artifact("implementation", ArtifactKind.Code, "v", ctx.Stage.Id, []))); })
            .Add("verifier", _ => Task.FromResult(StageResult.Success()))
            .Add("reviewer", ctx => Task.FromResult(StageResult.Malformed("no <artifact name=\"review\"> block", Feedback(ctx, "end with the tag"))));
        using var run = new TestRun(Loop, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeFalse();
        implementRuns.Should().Be(1, "a format failure is not a verdict about the implementation");
        run.EventsOfKind(EventKind.ReplanTriggered).Single()["accepted"].Should().Be("false");
        run.EventsOfKind(EventKind.ReplanTriggered).Single()["cause"].Should().StartWith("not a verdict");
    }
}
