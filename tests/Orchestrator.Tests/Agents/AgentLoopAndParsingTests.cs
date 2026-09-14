using Orchestrator.Agents.Agents;
using Orchestrator.Agents.Tools;
using Orchestrator.Tests.Fakes;

namespace Orchestrator.Tests.Agents;

public class AgentLoopAndParsingTests
{
    [Fact]
    public async Task Given_model_calls_a_tool_then_answers_When_loop_runs_Then_tool_result_is_fed_back_and_final_text_returned()
    {
        var workspace = new InMemoryWorkspace();
        await workspace.WriteFileAsync("a.cs", "class A {}", CancellationToken.None);
        var llm = new FakeLlmClient(FakeLlmClient.Call("read_file", new { path = "a.cs" }), FakeLlmClient.Text("done"));
        var loop = new AgentToolLoop(llm, new NullTrace(), _ => { });

        var outcome = await loop.RunAsync("stage", "r", "sys", "user", [new ReadFileTool(workspace)], CancellationToken.None);

        outcome.FinalText.Should().Be("done");
        outcome.ToolCalls.Should().Be(1);
        outcome.Iterations.Should().Be(2);
        var second = llm.Requests[1];
        second.Messages.Should().HaveCount(3);
        second.Messages[2].Should().BeOfType<Orchestrator.Agents.Llm.LlmMessage.ToolResults>()
            .Which.Results.Single().Content.Should().Be("class A {}");
    }

    [Fact]
    public async Task Given_unknown_tool_When_called_Then_error_result_is_returned_not_thrown()
    {
        var llm = new FakeLlmClient(FakeLlmClient.Call("format_disk", new { }), FakeLlmClient.Text("ok"));
        var loop = new AgentToolLoop(llm, new NullTrace(), _ => { });

        await loop.RunAsync("stage", "r", "sys", "user", [], CancellationToken.None);

        var results = (Orchestrator.Agents.Llm.LlmMessage.ToolResults)llm.Requests[1].Messages[2];
        results.Results.Single().IsError.Should().BeTrue();
        results.Results.Single().Content.Should().Contain("unknown tool");
    }

    [Fact]
    public async Task Given_model_never_stops_calling_tools_When_budget_spent_Then_loop_asks_for_final_answer_once()
    {
        var turns = Enumerable.Range(0, 3).Select(_ => FakeLlmClient.Call("list_files", new { })).Append(FakeLlmClient.Text("final")).ToArray();
        var llm = new FakeLlmClient(turns);
        var loop = new AgentToolLoop(llm, new NullTrace(), _ => { });

        var outcome = await loop.RunAsync("stage", "r", "sys", "user", [new ListFilesTool(new InMemoryWorkspace())], CancellationToken.None, maxIterations: 3);

        outcome.HitIterationLimit.Should().BeTrue();
        outcome.FinalText.Should().Be("final");
        llm.Requests.Last().Tools.Should().BeEmpty("the closing request must not offer tools");
    }

    [Fact]
    public void Given_final_text_with_artifact_blocks_When_parsed_Then_each_is_extracted_and_fences_are_stripped()
    {
        const string text = """
            Here is my work.
            <artifact name="spec">
            ```json
            {"title": "x"}
            ```
            </artifact>
            <artifact name="notes" format="markdown">
            # Notes
            </artifact>
            """;

        var artifacts = ArtifactParser.Extract(text);

        artifacts.Should().HaveCount(2);
        artifacts["spec"].Should().Be("""{"title": "x"}""");
        artifacts["notes"].Should().Be("# Notes");
    }

    [Fact]
    public void Given_missing_artifact_When_required_Then_throws_naming_it()
    {
        var act = () => ArtifactParser.Require(new Dictionary<string, string>(), "design");

        act.Should().Throw<AgentOutputException>().WithMessage("*<artifact name=\"design\">*");
    }
}
