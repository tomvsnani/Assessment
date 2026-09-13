using System.Net;
using System.Text.Json;
using Orchestrator.Agents.Llm;

namespace Orchestrator.Tests.Agents;

/// <summary>Wire-format check against a stub server: what we send, and how we read back text, tool calls and usage.</summary>
public class AnthropicClientTests
{
    private sealed class StubHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status) { Content = new StringContent(responseJson) };
        }
    }

    private const string ToolUseResponse = """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-opus-5",
         "content":[{"type":"thinking","thinking":"","signature":"sig"},{"type":"text","text":"Let me look."},
                    {"type":"tool_use","id":"toolu_1","name":"read_file","input":{"path":"a.cs"}}],
         "stop_reason":"tool_use","usage":{"input_tokens":120,"output_tokens":30}}
        """;

    [Fact]
    public async Task Given_request_with_tools_When_sent_Then_headers_and_body_match_the_messages_api()
    {
        var handler = new StubHandler(ToolUseResponse);
        var client = new AnthropicClient(new HttpClient(handler), "sk-test", "claude-opus-5");
        var tools = new[] { new ToolDefinition("read_file", "Read", JsonDocument.Parse("""{"type":"object","properties":{"path":{"type":"string"}}}""").RootElement) };

        var response = await client.CompleteAsync(new LlmRequest("be helpful", [new LlmMessage.UserText("hi")], tools), CancellationToken.None);

        handler.LastRequest!.Headers.GetValues("x-api-key").Should().Equal("sk-test");
        handler.LastRequest.Headers.GetValues("anthropic-version").Should().Equal("2023-06-01");
        var body = JsonDocument.Parse(handler.LastBody!).RootElement;
        body.GetProperty("model").GetString().Should().Be("claude-opus-5");
        body.GetProperty("system")[0].GetProperty("text").GetString().Should().Be("be helpful");
        body.GetProperty("system")[0].GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral");
        body.GetProperty("tools")[0].GetProperty("input_schema").GetProperty("type").GetString().Should().Be("object");
        body.GetProperty("messages")[0].GetProperty("content").GetString().Should().Be("hi");
        body.TryGetProperty("thinking", out _).Should().BeFalse("Opus 5 thinks adaptively by default; no explicit thinking config is sent");

        response.Turn.Text.Should().Be("Let me look.");
        response.Turn.ToolCalls.Should().ContainSingle().Which.Input.GetProperty("path").GetString().Should().Be("a.cs");
        response.StopReason.Should().Be("tool_use");
        response.InputTokens.Should().Be(120);
    }

    [Fact]
    public async Task Given_assistant_turn_with_thinking_block_When_echoed_back_Then_it_is_sent_verbatim()
    {
        var handler = new StubHandler(ToolUseResponse);
        var client = new AnthropicClient(new HttpClient(handler), "sk-test");
        var first = await client.CompleteAsync(new LlmRequest("s", [new LlmMessage.UserText("hi")], []), CancellationToken.None);

        await client.CompleteAsync(new LlmRequest("s",
            [new LlmMessage.UserText("hi"), first.Turn, new LlmMessage.ToolResults([new ToolResult("toolu_1", "read_file", "class A {}")])],
            []), CancellationToken.None);

        var messages = JsonDocument.Parse(handler.LastBody!).RootElement.GetProperty("messages");
        messages.GetArrayLength().Should().Be(3);
        messages[1].GetProperty("content")[0].GetProperty("type").GetString().Should().Be("thinking");
        messages[1].GetProperty("content")[0].GetProperty("signature").GetString().Should().Be("sig");
        messages[2].GetProperty("content")[0].GetProperty("tool_use_id").GetString().Should().Be("toolu_1");
    }

    [Fact]
    public async Task Given_429_When_sent_Then_throws_transient_LlmException()
    {
        var client = new AnthropicClient(new HttpClient(new StubHandler("""{"error":"rate"}""", HttpStatusCode.TooManyRequests)), "k");

        var act = () => client.CompleteAsync(new LlmRequest("s", [new LlmMessage.UserText("x")], []), CancellationToken.None);

        (await act.Should().ThrowAsync<LlmException>()).Which.IsTransient.Should().BeTrue();
    }
}
