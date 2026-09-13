using System.Text.Json;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Tools;

/// <summary>Compile everything in the workspace. Warnings are errors, exactly as in CI.</summary>
public sealed class RunBuildTool(IWorkspace workspace) : ITool
{
    public string Name => "run_build";
    public string Description => "Run `dotnet build` on the workspace solution and return the compiler output. Use it after writing code.";
    public string InputSchema => """{"type":"object","properties":{}}""";

    public async Task<string> InvokeAsync(JsonElement input, CancellationToken ct)
    {
        var result = await DotnetRunner.RunAsync(workspace.RootPath, "build --nologo -v q", TimeSpan.FromMinutes(4), ct);
        return (result.Succeeded ? "BUILD SUCCEEDED\n" : $"BUILD FAILED (exit {result.ExitCode})\n") + result.Output;
    }
}
