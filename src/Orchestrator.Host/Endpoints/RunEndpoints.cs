using Microsoft.AspNetCore.Mvc;
using Orchestrator.Core.Contracts;
using Orchestrator.Core.Governance;
using Orchestrator.Host.Runs;

namespace Orchestrator.Host.Endpoints;

/// <summary>Start, list, inspect, approve and stop runs.</summary>
public static class RunEndpoints
{
    public static void MapRuns(this IEndpointRouteBuilder app)
    {
        var runs = app.MapGroup("/api/runs");
        runs.MapGet("/", (RunRegistry registry) => Results.Ok(registry.List()));
        runs.MapPost("/", StartAsync);
        runs.MapGet("/{id}", Get);
        runs.MapGet("/{id}/artifacts/{name}", GetArtifactAsync);
        runs.MapPost("/{id}/approval", Approve);
        runs.MapPost("/{id}/stop", Stop);
    }

    private static async Task<IResult> StartAsync(RunRequest request, RunService service, IHostApplicationLifetime lifetime)
    {
        if (request.EffectiveApprover == "console")
        {
            return Results.BadRequest(new { error = "console approver is only available in headless mode; use web, replay or unattended" });
        }

        try
        {
            var handle = await service.StartAsync(request, null, null, lifetime.ApplicationStopping);
            return Results.Created($"/api/runs/{handle.Id}", new { id = handle.Id });
        }
        catch (Exception e) when (e is InvalidOperationException or FileNotFoundException or ArgumentException)
        {
            return Results.BadRequest(new { error = e.Message });
        }
    }

    private static IResult Get(string id, RunRegistry registry, RepositoryPaths paths)
    {
        if (registry.Active(id) is { } handle)
        {
            return Results.Ok(RunSummary.ForActive(handle));
        }

        return registry.DirectoryOf(id) is { } dir
            ? Results.Ok(RunSummary.ForHistorical(id, dir, paths))
            : Results.NotFound();
    }

    private static async Task<IResult> GetArtifactAsync(string id, string name, RunRegistry registry)
    {
        if (registry.Active(id)?.State.Artifact(name) is { } artifact)
        {
            return Results.Ok(new ArtifactContentView(artifact.Name, artifact.Kind.ToString(), artifact.ContentHash, artifact.Content));
        }

        var dir = registry.DirectoryOf(id);
        var file = dir is null ? null : Directory.GetFiles(Path.Combine(dir, "artifacts"), name + ".*").FirstOrDefault();
        if (file is null)
        {
            return Results.NotFound();
        }

        var content = await File.ReadAllTextAsync(file);
        return Results.Ok(new ArtifactContentView(name, Path.GetExtension(file) == ".json" ? "Json" : "Markdown", Artifact.Hash(content), content));
    }

    private static IResult Approve(string id, ApprovalBody body, RunRegistry registry)
    {
        var handle = registry.Active(id);
        if (handle?.WebApprover is null)
        {
            return Results.BadRequest(new { error = "this run does not take decisions from the dashboard" });
        }

        if (!Enum.TryParse<DecisionKind>(body.Kind, ignoreCase: true, out var kind) || kind == DecisionKind.OptionChosen)
        {
            return Results.BadRequest(new { error = "kind must be Approved, RevisionRequested or Rejected" });
        }

        if (kind != DecisionKind.Approved && string.IsNullOrWhiteSpace(body.Rationale))
        {
            return Results.BadRequest(new { error = "a rationale is required when not approving" });
        }

        var actor = string.IsNullOrWhiteSpace(body.Actor) ? Environment.GetEnvironmentVariable("SDLC_APPROVER") ?? Environment.UserName : body.Actor;
        var decision = new ApprovalDecision(kind, actor, string.IsNullOrWhiteSpace(body.Rationale) ? "approved" : body.Rationale,
            body.AmbiguityResolutions ?? new Dictionary<string, string>(StringComparer.Ordinal));

        return handle.WebApprover.TryResolve(decision)
            ? Results.Accepted()
            : Results.Conflict(new { error = "no approval is pending" });
    }

    private static IResult Stop(string id, RunRegistry registry, [FromBody] StopBody? body)
    {
        var handle = registry.Active(id);
        if (handle is null)
        {
            return Results.NotFound();
        }

        handle.Stop(body?.Reason is { Length: > 0 } r ? $"stopped from dashboard: {r}" : "stopped from dashboard");
        return Results.Accepted();
    }
}

public sealed record ApprovalBody(string Kind, string? Rationale, string? Actor, Dictionary<string, string>? AmbiguityResolutions);

public sealed record StopBody(string? Reason);
