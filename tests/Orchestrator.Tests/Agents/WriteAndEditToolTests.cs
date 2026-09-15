using System.Text.Json;
using Orchestrator.Agents.Tools;
using Orchestrator.Core.Governance.Policies;
using Orchestrator.Core.Workflow;
using Orchestrator.Tests.Fakes;

namespace Orchestrator.Tests.Agents;

/// <summary>
/// The write tools tell the agent at once when a file-content policy would block the stage, and
/// <c>edit_file</c> changes one exact, unique match instead of re-sending the whole file.
/// Seen live: 46 turns of work, then <c>pii-in-logs</c> blocked at the exit gate and the run failed.
/// </summary>
public class WriteAndEditToolTests
{
    private static readonly StageDefinition Stage = new("implement", "implementer", [], GateDefinition.Open, GateDefinition.Open, RetryDefinition.None, null, FailureHandling.StopRun);

    private static FilePolicyCheck Check() => new([new NoSecretsPolicy(), new PiiInLogsPolicy()], Stage);

    private static JsonElement Input(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Given_file_that_logs_a_url_When_written_Then_the_tool_result_warns_that_the_policy_will_block()
    {
        var workspace = new InMemoryWorkspace();
        var tool = new WriteFileTool(workspace, Check());

        var result = await tool.InvokeAsync(Input("""{"path":"src/Program.cs","content":"logger.LogWarning(\"Invalid URL submitted: {Url}\", url);"}"""), CancellationToken.None);

        result.Should().StartWith("wrote src/Program.cs");
        result.Should().Contain("pii-in-logs").And.Contain("BLOCK").And.Contain("'Url'");
        workspace.Files.Should().ContainKey("src/Program.cs", "the write still happens; the exit gate is the enforcement point");
    }

    [Fact]
    public async Task Given_clean_file_When_written_Then_no_warning()
    {
        var tool = new WriteFileTool(new InMemoryWorkspace(), Check());

        var result = await tool.InvokeAsync(Input("""{"path":"src/A.cs","content":"class A {}"}"""), CancellationToken.None);

        result.Should().Be("wrote src/A.cs");
    }

    [Fact]
    public async Task Given_unique_match_When_edited_Then_only_that_text_changes()
    {
        var workspace = new InMemoryWorkspace();
        await workspace.WriteFileAsync("src/A.cs", "var a = 1;\nvar b = 2;\nvar c = 3;\n", CancellationToken.None);
        var tool = new EditFileTool(workspace, Check());

        var result = await tool.InvokeAsync(Input("""{"path":"src/A.cs","old_string":"var b = 2;","new_string":"var b = 20;"}"""), CancellationToken.None);

        result.Should().Be("edited src/A.cs");
        workspace.Files["src/A.cs"].Should().Be("var a = 1;\nvar b = 20;\nvar c = 3;\n");
    }

    [Fact]
    public async Task Given_ambiguous_or_missing_match_When_edited_Then_nothing_changes_and_the_error_says_why()
    {
        var workspace = new InMemoryWorkspace();
        await workspace.WriteFileAsync("src/A.cs", "x = 1;\nx = 1;\n", CancellationToken.None);
        var tool = new EditFileTool(workspace);

        var ambiguous = await tool.InvokeAsync(Input("""{"path":"src/A.cs","old_string":"x = 1;","new_string":"x = 2;"}"""), CancellationToken.None);
        var missing = await tool.InvokeAsync(Input("""{"path":"src/A.cs","old_string":"y = 1;","new_string":"y = 2;"}"""), CancellationToken.None);
        var noFile = await tool.InvokeAsync(Input("""{"path":"src/B.cs","old_string":"x","new_string":"y"}"""), CancellationToken.None);

        ambiguous.Should().StartWith("ERROR").And.Contain("more than once");
        missing.Should().StartWith("ERROR").And.Contain("not found in");
        noFile.Should().StartWith("ERROR: file not found");
        workspace.Files["src/A.cs"].Should().Be("x = 1;\nx = 1;\n");
    }

    [Fact]
    public async Task Given_edit_that_introduces_a_secret_When_applied_Then_the_tool_result_warns()
    {
        var workspace = new InMemoryWorkspace();
        await workspace.WriteFileAsync("appsettings.json", "{ \"ApiKey\": \"\" }", CancellationToken.None);
        var tool = new EditFileTool(workspace, Check());

        var result = await tool.InvokeAsync(Input("""{"path":"appsettings.json","old_string":"\"ApiKey\": \"\"","new_string":"\"ApiKey\": \"sk-ant-api03-0123456789abcdefghijklmnopqrstuvwxyz\""}"""), CancellationToken.None);

        result.Should().StartWith("edited appsettings.json").And.Contain("no-secrets");
    }
}
