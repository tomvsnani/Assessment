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
        run.EventsOfKind(EventKind.CompensationRun).Should().NotBeEmpty("invalidated stages are compensated");
        ReliabilityMetrics.From(run.Events.All).Replans.Should().Be(1);
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
