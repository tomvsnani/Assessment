using System.Text.Json;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Tools;

/// <summary>Build and run every test project in the workspace.</summary>
public sealed class RunTestsTool(IWorkspace workspace) : ITool
{
    public string Name => "run_tests";
    public string Description => "Run `dotnet test` on the workspace solution and return the summary plus any failures.";
    public string InputSchema => """{"type":"object","properties":{}}""";

    public async Task<string> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        var result = await DotnetRunner.RunAsync(workspace.RootPath, "test --nologo -v q", TimeSpan.FromMinutes(6), ct);
        return (result.Succeeded ? "TESTS PASSED\n" : $"TESTS FAILED (exit {result.ExitCode})\n") + result.Output;
    }
}
