using System.Diagnostics;
using Orchestrator.Core.Governance;
using Orchestrator.Core.Workflow;
using Orchestrator.Host.Runs;

namespace Orchestrator.Host.Endpoints;

/// <summary>Scenario presets, workflow graphs, workspace baselines and audit verification.</summary>
public static class WorkflowEndpoints
{
    public static void MapWorkflows(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/scenarios", (RepositoryPaths paths) =>
            Results.Ok(ScenarioPreset.LoadAll(paths.Scenarios).Select(p => new
            {
                p.Name,
                p.Workflow,
                p.Baseline,
                Requirement = RequirementFile.Load(Path.Combine(paths.Root, p.RequirementPath), Core.Contracts.ScenarioKind.Greenfield),
                HasRecording = Directory.Exists(Path.Combine(paths.RecordingDirectory(p.Name), "llm")) && Directory.EnumerateFiles(Path.Combine(paths.RecordingDirectory(p.Name), "llm"), "*.json").Any(),
            })));

        app.MapGet("/api/baselines", (RepositoryPaths paths) => Results.Ok(Baselines(paths)));

        app.MapGet("/api/workflows", (RepositoryPaths paths) =>
            Results.Ok(Directory.GetFiles(paths.Workflows, "*.yaml").Select(f => Path.GetFileNameWithoutExtension(f)).Order(StringComparer.Ordinal)));

        app.MapGet("/api/workflows/{name}/graph", (string name, RepositoryPaths paths) =>
        {
            var file = paths.WorkflowFile(name);
            if (!File.Exists(file))
            {
                return Results.NotFound();
            }

            var workflow = WorkflowLoader.Load(file);
            var graph = new DependencyGraph(workflow.Stages);
            return Results.Ok(new
            {
                workflow.Name,
                workflow.MaxParallelStages,
                Levels = graph.ParallelLevels(),
                Stages = graph.Stages.Select(s => new
                {
                    s.Id,
                    s.Agent,
                    s.DependsOn,
                    Entry = new { s.Entry.RequiredArtifacts, s.Entry.Policies, s.Entry.Approval },
                    Exit = new { s.Exit.RequiredArtifacts, s.Exit.Policies, s.Exit.Approval },
                    Retry = new { s.Retry.MaxAttempts, BaseDelaySeconds = s.Retry.BaseDelay.TotalSeconds },
                    s.FallbackAgent,
                    OnFailure = new { s.OnFailure.RerunFrom, s.OnFailure.MaxLoops },
                }),
                Yaml = File.ReadAllText(file),
            });
        });

        app.MapGet("/api/runs/{id}/audit", (string id, RunRegistry registry) =>
        {
            var dir = registry.DirectoryOf(id);
            var file = dir is null ? null : Path.Combine(dir, "audit.jsonl");
            if (file is null || !File.Exists(file))
            {
                return Results.NotFound();
            }

            var entries = AuditLog.ReadFile(file);
            return Results.Ok(new { Intact = AuditLog.Verify(entries) is null, BrokenAt = AuditLog.Verify(entries), Entries = entries });
        });
    }

    /// <summary>scaffold, plus every git tag and HEAD — anything a brownfield run can start from.</summary>
    private static IEnumerable<object> Baselines(RepositoryPaths paths)
    {
        yield return new { Id = "scaffold", Description = "no code: build configuration only (greenfield)" };
        yield return new { Id = "git:HEAD", Description = "the repository as it is now" };
        var psi = new ProcessStartInfo("git", "tag --list") { WorkingDirectory = paths.Root, RedirectStandardOutput = true, UseShellExecute = false };
        using var process = Process.Start(psi);
        if (process is null)
        {
            yield break;
        }

        foreach (var tag in process.StandardOutput.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return new { Id = "git:" + tag, Description = $"tag {tag}" };
        }
    }
}
