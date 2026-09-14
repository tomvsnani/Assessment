using Orchestrator.Core.Governance;
using Orchestrator.Core.Workflow;
using Orchestrator.Host.Runs;

namespace Orchestrator.Host.Endpoints;

/// <summary>Workflow definitions, their graphs, and audit verification.</summary>
public static class WorkflowEndpoints
{
    public static void MapWorkflows(this IEndpointRouteBuilder app)
    {
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
                Kind = workflow.Kind.ToString(),
                workflow.Baseline,
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
                Requirement = File.ReadAllText(Path.Combine(paths.Root, workflow.RequirementPath)),
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
}
