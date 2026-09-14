using Orchestrator.Agents.Tools;
using Orchestrator.Core.Contracts;

namespace Orchestrator.Agents.Agents;

/// <summary>
/// Deterministic, no LLM: builds and runs the workspace tests. A failing run fails the stage with
/// the test output as feedback, which the workflow routes back to the implementer.
/// Proof that "agent" in this runtime means "a role in the graph", not "a model".
/// </summary>
public sealed class VerifierAgent(IRunTrace trace, Action<string> log) : IStageAgent
{
    public const string ArtifactName = "test-report";

    public string Role => "verifier";

    public async Task<StageResult> ExecuteAsync(StageContext context)
    {
        var ct = context.CancellationToken;
        var root = context.Workspace.RootPath;

        trace.ToolStarted(context.Stage.Id, Role, "dotnet build", "{}");
        var buildWatch = System.Diagnostics.Stopwatch.StartNew();
        var build = await DotnetRunner.RunAsync(root, "build --nologo -v q", TimeSpan.FromMinutes(4), ct);
        trace.ToolInvoked(context.Stage.Id, Role, "dotnet build", "{}", Preview(build.Output), !build.Succeeded, buildWatch.Elapsed);
        if (!build.Succeeded)
        {
            log("verifier: build failed");
            return Fail(context, "build failed", "## Build\nFAILED\n```\n" + build.Output + "\n```");
        }

        trace.ToolStarted(context.Stage.Id, Role, "dotnet test", "{}");
        var testWatch = System.Diagnostics.Stopwatch.StartNew();
        var tests = await DotnetRunner.RunAsync(root, "test --no-build --nologo -v q", TimeSpan.FromMinutes(6), ct);
        trace.ToolInvoked(context.Stage.Id, Role, "dotnet test", "{}", Preview(tests.Output), !tests.Succeeded, testWatch.Elapsed);
        var report = "## Build\nsucceeded\n\n## Tests\n" + (tests.Succeeded ? "PASSED" : "FAILED") + "\n```\n" + tests.Output + "\n```";
        if (!tests.Succeeded)
        {
            log("verifier: tests failed");
            return Fail(context, "tests failed", report);
        }

        log("verifier: build and tests passed");
        return StageResult.Success(new Artifact(ArtifactName, ArtifactKind.TestReport, report, context.Stage.Id, [ImplementerAgent.ArtifactName]));
    }

    private static string Preview(string output) => output.Length <= 600 ? output : output[..600] + "...";

    private static StageResult Fail(StageContext context, string reason, string report) =>
        StageResult.Failure(reason,
            new Artifact("feedback", ArtifactKind.Feedback, "The verifier ran the build and tests on your implementation:\n\n" + report, context.Stage.Id, [ImplementerAgent.ArtifactName]));
}
