using Orchestrator.Agents.Llm;
using Orchestrator.Tests.Fakes;

namespace Orchestrator.Tests.Agents;

public class RecordingClientTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sdlc-rec-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _reports = [];

    private static LlmRequest Request(string text) => new("system", [new LlmMessage.UserText(text)], []);

    [Fact]
    public async Task Given_recorded_exchanges_When_replayed_with_same_requests_Then_responses_match_exactly_without_inner_client()
    {
        var inner = new FakeLlmClient(FakeLlmClient.Text("first"), FakeLlmClient.Call("read_file", new { path = "a.cs" }));
        var recorder = RecordingClient.Record(inner, _dir, _reports.Add);
        recorder.CurrentLabel = "requirements";
        await recorder.CompleteAsync(Request("hello"), CancellationToken.None);
        await recorder.CompleteAsync(Request("again"), CancellationToken.None);

        var replay = RecordingClient.Replay(_dir, _reports.Add);
        replay.CurrentLabel = "requirements";
        var first = await replay.CompleteAsync(Request("hello"), CancellationToken.None);
        var second = await replay.CompleteAsync(Request("again"), CancellationToken.None);

        first.Turn.Text.Should().Be("first");
        second.Turn.ToolCalls.Should().ContainSingle().Which.Name.Should().Be("read_file");
        second.Turn.ToolCalls[0].Input.GetProperty("path").GetString().Should().Be("a.cs");
        replay.ExactMatches.Should().Be(2);
        replay.SequenceMatches.Should().Be(0);
        replay.Provider.Should().Be("fake");
        Directory.GetFiles(_dir).Should().HaveCount(2).And.AllSatisfy(f => Path.GetFileName(f).Should().Contain("-requirements-"));
    }

    [Fact]
    public async Task Given_request_drifted_When_replayed_Then_falls_back_to_sequence_and_reports_it()
    {
        var recorder = RecordingClient.Record(new FakeLlmClient(FakeLlmClient.Text("answer")), _dir, _reports.Add);
        recorder.CurrentLabel = "implementer";
        await recorder.CompleteAsync(Request("build output at 12:00:01"), CancellationToken.None);

        var replay = RecordingClient.Replay(_dir, _reports.Add);
        replay.CurrentLabel = "implementer";
        var response = await replay.CompleteAsync(Request("build output at 12:00:07"), CancellationToken.None);

        response.Turn.Text.Should().Be("answer");
        replay.SequenceMatches.Should().Be(1);
        _reports.Should().ContainSingle(r => r.Contains("input drifted", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Given_no_recording_for_label_When_replayed_Then_throws_with_guidance()
    {
        var replay = RecordingClient.Replay(_dir, _reports.Add);
        replay.CurrentLabel = "architect";

        var act = () => replay.CompleteAsync(Request("x"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No recording left for 'architect'*--live*");
    }

    [Fact]
    public void Given_same_content_When_hashed_Then_hash_is_stable_and_ignores_tool_schemas_but_not_tool_names()
    {
        var a = RecordingClient.HashRequest(Request("x"));
        var b = RecordingClient.HashRequest(Request("x"));
        var c = RecordingClient.HashRequest(Request("y"));

        a.Should().Be(b);
        a.Should().NotBe(c);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }
}
