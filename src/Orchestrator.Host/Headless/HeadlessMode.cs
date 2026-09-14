using Orchestrator.Core.Governance;
using Orchestrator.Core.Metrics;
using Orchestrator.Core.Workflow;
using Orchestrator.Host.Approvals;
using Orchestrator.Host.Runs;

namespace Orchestrator.Host.Headless;

/// <summary>
/// The same service without a port: <c>sdlc run &lt;scenario&gt; [--live] [--unattended] [--provider p] [--model m]</c>,
/// <c>sdlc graph &lt;scenario&gt;</c>, <c>sdlc verify-audit &lt;runs/dir&gt;</c>. Used by CI and by graders who
/// prefer a terminal; approvals come from the console.
/// </summary>
public static class HeadlessMode
{
    public const string Usage = """
        usage:
          sdlc                                   start the API + dashboard on http://localhost:5100
          sdlc run <scenario> [--live] [--unattended] [--provider anthropic|openai|gemini] [--model <id>]
          sdlc graph <scenario>
          sdlc verify-audit <runs/dir>
        """;

    public static async Task<int> RunAsync(string[] args, RunService service, RepositoryPaths paths, CancellationToken ctrlC)
    {
        switch (args[0])
        {
            case "run" when args.Length >= 2:
                return await RunScenarioAsync(args, service, ctrlC);
            case "graph" when args.Length == 2:
                return Graph(args[1], paths);
            case "verify-audit" when args.Length == 2:
                return VerifyAudit(args[1]);
            default:
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    private static async Task<int> RunScenarioAsync(string[] args, RunService service, CancellationToken ctrlC)
    {
        var live = args.Contains("--live");
        var unattended = args.Contains("--unattended");
        var provider = ValueOf(args, "--provider");
        var model = ValueOf(args, "--model");
        var request = new RunRequest(args[1], live, provider, model, live ? (unattended ? "unattended" : "console") : "replay");

        Console.WriteLine($"mode: {(live ? $"LIVE via {request.EffectiveProvider}" : "REPLAY from committed recordings")}");
        var console = new ConsoleApprover(Environment.GetEnvironmentVariable("SDLC_APPROVER") ?? Environment.UserName);
        var handle = await service.StartAsync(request, console, line => Console.WriteLine($"          {line}"), ctrlC);
        using var renderer = new ConsoleRenderer(handle.Events);
        Console.WriteLine($"run: {handle.Id}   workspace baseline: {handle.Workflow.Baseline}");

        var outcome = await handle.Completion;
        Console.WriteLine();
        Console.WriteLine(ReliabilityMetrics.From(handle.Events.All).Render());
        Console.WriteLine($"replay fidelity: {handle.Llm.ExactMatches} exact, {handle.Llm.SequenceMatches} by sequence");
        Console.WriteLine($"outputs: {handle.RunDirectory}");
        Console.WriteLine(outcome.Succeeded ? "RESULT: success" : $"RESULT: {outcome.Summary}");
        return outcome.Succeeded ? 0 : 1;
    }

    private static int Graph(string scenario, RepositoryPaths paths)
    {
        var workflow = WorkflowLoader.Load(paths.WorkflowFile(scenario));
        var graph = new DependencyGraph(workflow.Stages);
        Console.WriteLine($"{workflow.Name} ({workflow.Kind}) baseline={workflow.Baseline} max_parallel={workflow.MaxParallelStages}");
        foreach (var (level, index) in graph.ParallelLevels().Select((l, i) => (l, i)))
        {
            Console.WriteLine($"level {index}: {string.Join("  ‖  ", level)}");
        }

        foreach (var stage in graph.Stages)
        {
            Console.WriteLine($"{stage.Id}: agent={stage.Agent} depends_on=[{string.Join(", ", stage.DependsOn)}] " +
                              $"entry={Describe(stage.Entry)} exit={Describe(stage.Exit)} retry={stage.Retry.MaxAttempts}" +
                              (stage.FallbackAgent is { } f ? $" fallback={f}" : string.Empty) +
                              (stage.OnFailure.RerunFrom is { } r ? $" on_failure=rerun-from:{r}x{stage.OnFailure.MaxLoops}" : string.Empty));
        }

        return 0;
    }

    private static int VerifyAudit(string target)
    {
        var file = Directory.Exists(target) ? Path.Combine(target, "audit.jsonl") : target;
        if (!File.Exists(file))
        {
            Console.Error.WriteLine($"No audit.jsonl at {file}");
            return 1;
        }

        var entries = AuditLog.ReadFile(file);
        if (AuditLog.Verify(entries) is { } seq)
        {
            Console.WriteLine($"TAMPERED: chain breaks at entry seq={seq} of {entries.Count}");
            return 1;
        }

        Console.WriteLine($"INTACT: {entries.Count} audit entries, chain verified");
        foreach (var decision in entries.Where(e => e.Action == "ApprovalDecided"))
        {
            Console.WriteLine($"  {decision.At:u} {decision.StageId,-14} {decision.Outcome,-18} by {decision.Actor}: {decision.Detail}");
        }

        return 0;
    }

    private static string Describe(GateDefinition gate)
    {
        var parts = new List<string>();
        if (gate.RequiredArtifacts.Count > 0)
        {
            parts.Add($"artifacts[{string.Join(",", gate.RequiredArtifacts)}]");
        }

        if (gate.Policies.Count > 0)
        {
            parts.Add($"policies[{string.Join(",", gate.Policies)}]");
        }

        if (gate.Approval is not null)
        {
            parts.Add($"APPROVAL:{gate.Approval}");
        }

        return parts.Count == 0 ? "open" : string.Join("+", parts);
    }

    private static string? ValueOf(string[] args, string flag)
    {
        var index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
