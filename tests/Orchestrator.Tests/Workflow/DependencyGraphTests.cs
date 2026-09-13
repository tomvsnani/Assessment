using Orchestrator.Core.Workflow;

namespace Orchestrator.Tests.Workflow;

public class DependencyGraphTests
{
    private static StageDefinition Stage(string id, params string[] dependsOn) =>
        new(id, "any", dependsOn, GateDefinition.Open, GateDefinition.Open, RetryDefinition.None, null, FailureHandling.StopRun);

    // requirements -> design -> plan -> {implement, test-plan} -> verify -> {review, docs} -> release
    private static readonly DependencyGraph Sdlc = new([
        Stage("requirements"),
        Stage("design", "requirements"),
        Stage("plan", "design"),
        Stage("implement", "plan"),
        Stage("test-plan", "plan"),
        Stage("verify", "implement", "test-plan"),
        Stage("review", "verify"),
        Stage("docs", "verify"),
        Stage("release", "review", "docs"),
    ]);

    [Fact]
    public void Given_dag_When_ordered_Then_every_stage_follows_its_dependencies()
    {
        var order = Sdlc.TopologicalOrder.ToList();

        foreach (var stage in Sdlc.Stages)
        {
            foreach (var dependency in stage.DependsOn)
            {
                order.IndexOf(dependency).Should().BeLessThan(order.IndexOf(stage.Id), $"{dependency} must precede {stage.Id}");
            }
        }
    }

    [Fact]
    public void Given_nothing_complete_When_ready_Then_only_roots()
    {
        Sdlc.Ready(new HashSet<string>(), new HashSet<string>()).Select(s => s.Id).Should().Equal("requirements");
    }

    [Fact]
    public void Given_plan_complete_When_ready_Then_both_parallel_branches_are_ready()
    {
        var completed = new HashSet<string> { "requirements", "design", "plan" };

        Sdlc.Ready(completed, new HashSet<string>()).Select(s => s.Id).Should().BeEquivalentTo("implement", "test-plan");
    }

    [Fact]
    public void Given_one_branch_running_When_ready_Then_join_stage_is_not_ready()
    {
        var completed = new HashSet<string> { "requirements", "design", "plan", "implement" };
        var running = new HashSet<string> { "test-plan" };

        Sdlc.Ready(completed, running).Should().BeEmpty();
    }

    [Fact]
    public void Given_stage_When_downstream_Then_returns_transitive_dependents_only()
    {
        Sdlc.Downstream("plan").Should().BeEquivalentTo("implement", "test-plan", "verify", "review", "docs", "release");
        Sdlc.Downstream("release").Should().BeEmpty();
    }

    [Fact]
    public void Given_dag_When_levelled_Then_parallel_groups_are_visible()
    {
        var levels = Sdlc.ParallelLevels();

        levels[3].Should().BeEquivalentTo("implement", "test-plan");
        levels[5].Should().BeEquivalentTo("review", "docs");
    }

    [Fact]
    public void Given_cycle_When_constructed_Then_throws_naming_the_stages()
    {
        var act = () => new DependencyGraph([Stage("a", "c"), Stage("b", "a"), Stage("c", "b")]);

        act.Should().Throw<WorkflowValidationException>().WithMessage("*cycle*a, b, c*");
    }

    [Fact]
    public void Given_unknown_dependency_When_constructed_Then_throws()
    {
        var act = () => new DependencyGraph([Stage("a", "ghost")]);

        act.Should().Throw<WorkflowValidationException>().WithMessage("*unknown stage 'ghost'*");
    }

    [Fact]
    public void Given_duplicate_id_When_constructed_Then_throws()
    {
        var act = () => new DependencyGraph([Stage("a"), Stage("a")]);

        act.Should().Throw<WorkflowValidationException>().WithMessage("*declared twice*");
    }
}
