using Orchestrator.Core.Workflow;

namespace Orchestrator.Tests.Workflow;

public class WorkflowLoaderTests
{
    private const string Yaml = """
        name: sample
        max_parallel: 2
        stages:
          - id: requirements
            agent: requirements
            exit:
              artifacts: [spec]
              policies: [no-secrets]
              approval: approve-spec
          - id: implement
            agent: implementer
            depends_on: [requirements]
            retry:
              max_attempts: 3
              base_delay_seconds: 0.5
            fallback: implementer-conservative
            exit:
              artifacts: [implementation]
          - id: verify
            agent: verifier
            depends_on: [implement]
            on_failure:
              rerun_from: implement
              max_loops: 2
        """;

    [Fact]
    public void Given_yaml_When_parsed_Then_workflow_fields_are_mapped()
    {
        var workflow = WorkflowLoader.Parse(Yaml);

        workflow.Name.Should().Be("sample");
        workflow.MaxParallelStages.Should().Be(2);
        workflow.Stages.Should().HaveCount(3);
    }

    [Fact]
    public void Given_yaml_When_parsed_Then_gates_retry_fallback_and_failure_handling_are_mapped()
    {
        var workflow = WorkflowLoader.Parse(Yaml);
        var requirements = workflow.Stages[0];
        var implement = workflow.Stages[1];
        var verify = workflow.Stages[2];

        requirements.Exit.RequiredArtifacts.Should().Equal("spec");
        requirements.Exit.Policies.Should().Equal("no-secrets");
        requirements.Exit.Approval.Should().Be("approve-spec");
        requirements.IsHighImpact.Should().BeTrue();

        implement.DependsOn.Should().Equal("requirements");
        implement.Retry.Should().Be(new RetryDefinition(3, TimeSpan.FromSeconds(0.5)));
        implement.FallbackAgent.Should().Be("implementer-conservative");
        implement.IsHighImpact.Should().BeFalse();

        verify.OnFailure.Should().Be(new FailureHandling("implement", 2));
        verify.Retry.Should().Be(RetryDefinition.None);
    }

    [Fact]
    public void Given_missing_stage_agent_When_parsed_Then_throws()
    {
        var act = () => WorkflowLoader.Parse("name: x\nrequirement: r.md\nstages:\n  - id: a\n");

        act.Should().Throw<WorkflowValidationException>().WithMessage("*id and an agent*");
    }

    [Fact]
    public void Given_cyclic_yaml_When_parsed_Then_throws()
    {
        var act = () => WorkflowLoader.Parse("name: x\nrequirement: r.md\nstages:\n  - {id: a, agent: x, depends_on: [b]}\n  - {id: b, agent: x, depends_on: [a]}\n");

        act.Should().Throw<WorkflowValidationException>().WithMessage("*cycle*");
    }
}
