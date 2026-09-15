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

    // Seen live (greenfield-20260915-162943): the whole spec as one ```json fence, no <artifact> tag, twice in a row.
    [Fact]
    public void Given_message_that_is_one_fenced_document_and_no_tag_When_recovered_Then_the_fence_body_is_the_artifact()
    {
        const string text = """
            ```json
            {
              "title": "URL Shortener Service, First Version",
              "assumptions": []
            }
            ```

            """;

        ArtifactParser.RecoverSingle(text).Should().Be("""
            {
              "title": "URL Shortener Service, First Version",
              "assumptions": []
            }
            """);
    }

    [Fact]
    public void Given_message_that_is_bare_json_When_recovered_Then_it_is_the_artifact()
    {
        ArtifactParser.RecoverSingle("  {\"title\": \"x\"} \n").Should().Be("{\"title\": \"x\"}");
        ArtifactParser.RecoverSingle("[{\"key\": \"WI-1\"}]").Should().Be("[{\"key\": \"WI-1\"}]");
    }

    // Seen live (greenfield-20260915-163443): the implementer's whole report, "## Summary … ## Work items …", no tag.
    [Fact]
    public void Given_message_that_is_a_markdown_report_When_recovered_Then_the_whole_report_is_the_artifact()
    {
        const string report = "## Summary\nBuilt it.\n\n## Work items\n- WI-1: done, see\n```csharp\nvar x = 1;\n```\n- WI-2: done\n```csharp\nvar y = 2;\n```\n\n## Test run\nPassed 26";

        ArtifactParser.RecoverSingle(report + "\n").Should().Be(report, "a report that opens with a heading is one document even when it quotes several code samples");
    }

    [Theory]
    [InlineData("")]
    [InlineData("I could not produce the spec because the requirement is empty.")]
    [InlineData("Here is the spec you asked for:\n\n## Summary\nprose before the heading means commentary, not a document")]
    [InlineData("First:\n```json\n{}\n```\nSecond:\n```json\n[]\n```")]
    public void Given_prose_empty_or_ambiguous_message_When_recovered_Then_nothing_is_recovered(string text)
    {
        ArtifactParser.RecoverSingle(text).Should().BeNull("only an unambiguous single document may stand in for the tagged artifact");
    }
}

public class TruncatedAnswerTests
{
    private sealed class TruncatingClient : Orchestrator.Agents.Llm.ILlmClient
    {
        private int _calls;
        public List<Orchestrator.Agents.Llm.LlmRequest> Requests { get; } = [];
        public string Provider => "fake";
        public string Model => "fake";

        public Task<Orchestrator.Agents.Llm.LlmResponse> CompleteAsync(Orchestrator.Agents.Llm.LlmRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            var first = _calls++ == 0;
            var turn = FakeLlmClient.Text(first ? "<artifact name=\"design\">part one, " : "part two</artifact>");
            return Task.FromResult(new Orchestrator.Agents.Llm.LlmResponse(turn, first ? "max_tokens" : "end_turn", 5, 5));
        }
    }

    [Fact]
    public async Task Given_answer_cut_off_by_output_limit_When_loop_runs_Then_it_asks_to_continue_and_stitches_the_text()
    {
        var client = new TruncatingClient();
        var loop = new AgentToolLoop(client, new NullTrace(), _ => { });

        var outcome = await loop.RunAsync("design", "architect", "sys", "user", [], CancellationToken.None);

        outcome.FinalText.Should().Be("<artifact name=\"design\">part one, part two</artifact>");
        client.Requests.Should().HaveCount(2);
        client.Requests[1].Messages[^1].Should().BeOfType<Orchestrator.Agents.Llm.LlmMessage.UserText>()
            .Which.Text.Should().Contain("cut off");
        ArtifactParser.Extract(outcome.FinalText)["design"].Should().Be("part one, part two");
    }
}
