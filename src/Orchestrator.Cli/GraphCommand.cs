using Orchestrator.Core.Workflow;

namespace Orchestrator.Cli;

/// <summary>Prints a scenario's stage graph: order, parallel groups, gates, retry and failure handling.</summary>
public static class GraphCommand
{
    public static int Execute(CliOptions options)
    {
        var paths = RepositoryPaths.Locate();
        var workflow = WorkflowLoader.Load(paths.WorkflowFile(options.Target));
        var graph = new DependencyGraph(workflow.Stages);

        Console.WriteLine($"{workflow.Name} ({workflow.Kind}) baseline={workflow.Baseline} max_parallel={workflow.MaxParallelStages}");
        Console.WriteLine();
        foreach (var (level, index) in graph.ParallelLevels().Select((l, i) => (l, i)))
        {
            Console.WriteLine($"level {index}: {string.Join("  ‖  ", level)}");
        }

        Console.WriteLine();
        foreach (var stage in graph.Stages)
        {
            Console.WriteLine($"{stage.Id}  agent={stage.Agent}  depends_on=[{string.Join(", ", stage.DependsOn)}]");
            Console.WriteLine($"  entry: {Describe(stage.Entry)}");
            Console.WriteLine($"  exit : {Describe(stage.Exit)}");
            Console.WriteLine($"  retry: {stage.Retry.MaxAttempts} attempt(s)" +
                              (stage.FallbackAgent is { } f ? $", fallback={f}" : string.Empty) +
                              (stage.OnFailure.RerunFrom is { } r ? $", on_failure: rerun from {r} up to {stage.OnFailure.MaxLoops}x" : ", on_failure: stop run"));
        }

        return 0;
    }

    private static string Describe(GateDefinition gate)
    {
        var parts = new List<string>();
        if (gate.RequiredArtifacts.Count > 0)
        {
            parts.Add($"artifacts=[{string.Join(", ", gate.RequiredArtifacts)}]");
        }

        if (gate.Policies.Count > 0)
        {
            parts.Add($"policies=[{string.Join(", ", gate.Policies)}]");
        }

        if (gate.Approval is not null)
        {
            parts.Add($"HUMAN APPROVAL '{gate.Approval}'");
        }

        return parts.Count == 0 ? "open" : string.Join("  ", parts);
    }
}
