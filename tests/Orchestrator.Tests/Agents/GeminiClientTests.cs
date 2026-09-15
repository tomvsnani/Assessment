using System.Net;
using Orchestrator.Agents.Llm;

namespace Orchestrator.Tests.Agents;

/// <summary>
/// Gemini can answer 200 with nothing usable. Seen live: a candidate with empty parts (the agent
/// then "finished" without an artifact) and a candidate with no <c>content</c> at all (a
/// <c>KeyNotFoundException</c> that failed the stage). Both must surface as transient provider
/// failures that the tool loop retries.
/// </summary>
public class GeminiClientTests
{
    private sealed class StubHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(responseJson) });
    }

    private static GeminiClient Client(string json) => new(new HttpClient(new StubHandler(json)), "key", "gemini-2.5-flash");

    private static LlmRequest Request() => new("s", [new LlmMessage.UserText("hi")], []);

    [Fact]
    public async Task Given_function_call_response_When_parsed_Then_tool_call_and_usage_are_read()
    {
        var client = Client("""
            {"candidates":[{"content":{"role":"model","parts":[{"functionCall":{"name":"run_build","args":{}}}]},"finishReason":"STOP"}],
             "usageMetadata":{"promptTokenCount":56087,"candidatesTokenCount":10}}
            """);

        var response = await client.CompleteAsync(Request(), CancellationToken.None);

        response.Turn.ToolCalls.Should().ContainSingle().Which.Name.Should().Be("run_build");
        response.StopReason.Should().Be("tool_use");
        response.InputTokens.Should().Be(56087);
    }

    [Theory]
    [InlineData("""{"promptFeedback":{"blockReason":"OTHER"},"usageMetadata":{"promptTokenCount":10}}""", "no candidates")]
    [InlineData("""{"candidates":[{"finishReason":"SAFETY","index":0}],"usageMetadata":{"promptTokenCount":10}}""", "without content")]
    [InlineData("""{"candidates":[{"content":{"role":"model"},"finishReason":"MAX_TOKENS"}],"usageMetadata":{"promptTokenCount":28295,"candidatesTokenCount":0}}""", "no text and no function call")]
    public async Task Given_200_with_nothing_usable_When_parsed_Then_it_is_a_transient_provider_failure(string json, string expectedReason)
    {
        var client = Client(json);

        var act = () => client.CompleteAsync(Request(), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<LlmException>();
        thrown.Which.IsTransient.Should().BeTrue("the tool loop retries transient failures instead of failing the stage");
        thrown.Which.Message.Should().Contain(expectedReason);
    }
}
