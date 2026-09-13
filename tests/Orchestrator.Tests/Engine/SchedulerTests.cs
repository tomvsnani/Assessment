using Orchestrator.Core.Contracts;
using Orchestrator.Core.State;
using Orchestrator.Tests.Fakes;

namespace Orchestrator.Tests.Engine;

public class SchedulerTests
{
    private const string Linear = """
        name: linear
        requirement: r.md
        stages:
          - { id: a, agent: worker-a, exit: { artifacts: [alpha] } }
          - { id: b, agent: worker-b, depends_on: [a], exit: { artifacts: [beta] } }
          - { id: c, agent: worker-c, depends_on: [b] }
        """;

    private const string Diamond = """
        name: diamond
        requirement: r.md
        max_parallel: 4
        stages:
          - { id: plan, agent: planner }
          - { id: left, agent: left, depends_on: [plan] }
          - { id: right, agent: right, depends_on: [plan] }
          - { id: join, agent: joiner, depends_on: [left, right] }
        """;

    [Fact]
    public async Task Given_linear_workflow_When_run_Then_stages_complete_in_dependency_order_and_artifacts_flow_downstream()
    {
        string? seenByC = null;
        var agents = new FakeAgents()
            .Producing("worker-a", "alpha")
            .Add("worker-b", ctx => Task.FromResult(StageResult.Success(
                new Artifact("beta", ArtifactKind.Design, "beta from " + ctx.Require("alpha").Content, ctx.Stage.Id, ["alpha"]))))
            .Add("worker-c", ctx =>
            {
                seenByC = ctx.Require("beta").Content;
                return Task.FromResult(StageResult.Success());
            });
        using var run = new TestRun(Linear, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeTrue();
        run.StageOrder().Should().Equal("a", "b", "c");
        seenByC.Should().Be("beta from alpha by worker-a");
        run.EventsOfKind(EventKind.RunCompleted).Should().ContainSingle();
    }

    [Fact]
    public async Task Given_diamond_workflow_When_run_Then_branches_overlap_and_join_waits_for_both()
    {
        // Each branch signals it started, then waits until the other has started: only possible if they run concurrently.
        var leftStarted = new TaskCompletionSource();
        var rightStarted = new TaskCompletionSource();
        var agents = new FakeAgents()
            .Producing("planner", "plan")
            .Add("left", async _ => { leftStarted.SetResult(); await rightStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); return StageResult.Success(); })
            .Add("right", async _ => { rightStarted.SetResult(); await leftStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)); return StageResult.Success(); })
            .Producing("joiner", "joined");
        using var run = new TestRun(Diamond, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeTrue();
        var order = run.StageOrder().ToList();
        order.First().Should().Be("plan");
        order.Last().Should().Be("join");
        order.Skip(1).Take(2).Should().BeEquivalentTo("left", "right");
    }

    [Fact]
    public async Task Given_max_parallel_one_When_diamond_run_Then_branches_never_overlap()
    {
        var concurrent = 0;
        var peak = 0;
        Task<StageResult> Track()
        {
            peak = Math.Max(peak, Interlocked.Increment(ref concurrent));
            Thread.Sleep(20);
            Interlocked.Decrement(ref concurrent);
            return Task.FromResult(StageResult.Success());
        }

        var agents = new FakeAgents()
            .Producing("planner", "plan")
            .Add("left", _ => Track())
            .Add("right", _ => Track())
            .Producing("joiner", "joined");
        using var run = new TestRun(Diamond.Replace("max_parallel: 4", "max_parallel: 1", StringComparison.Ordinal), agents);

        await run.RunAsync();

        peak.Should().Be(1);
    }

    [Fact]
    public async Task Given_agent_throws_and_no_retry_When_run_Then_run_fails_rolls_back_and_workspace_is_restored()
    {
        var agents = new FakeAgents()
            .Add("worker-a", async ctx =>
            {
                await ctx.Workspace.WriteFileAsync("src/a.cs", "class A {}", ctx.CancellationToken);
                return StageResult.Success(new Artifact("alpha", ArtifactKind.Code, "a", ctx.Stage.Id, []));
            })
            .Add("worker-b", _ => throw new InvalidOperationException("boom"))
            .Producing("worker-c", "gamma");
        using var run = new TestRun(Linear, agents);

        var outcome = await run.RunAsync();

        outcome.Succeeded.Should().BeFalse();
        outcome.Summary.Should().Contain("boom");
        run.EventsOfKind(EventKind.SafeStopTriggered).Should().ContainSingle();
        run.EventsOfKind(EventKind.RollbackCompleted).Should().ContainSingle();
        run.EventsOfKind(EventKind.CompensationRun).Select(e => e.StageId).Should().Equal("a");
        run.Workspace.Files.Should().BeEmpty("stage a's file must be undone by the rollback");
        run.StageOrder(EventKind.StageStarted).Should().NotContain("c");
    }

    [Fact]
    public async Task Given_ctrl_c_While_stage_running_Then_run_stops_safely_and_rolls_back()
    {
        var agents = new FakeAgents()
            .Producing("worker-a", "alpha")
            .Add("worker-b", async ctx => { await Task.Delay(Timeout.InfiniteTimeSpan, ctx.CancellationToken); return StageResult.Success(); })
            .Producing("worker-c", "gamma");
        using var run = new TestRun(Linear, agents);

        var running = run.RunAsync();
        await Task.Delay(50);
        run.SafeStop.Trigger("operator pressed Ctrl+C");
        var outcome = await running;

        outcome.Succeeded.Should().BeFalse();
        outcome.Summary.Should().Contain("Ctrl+C");
        run.EventsOfKind(EventKind.RollbackCompleted).Should().ContainSingle();
        run.EventsOfKind(EventKind.RunFailed).Should().ContainSingle();
    }
}
