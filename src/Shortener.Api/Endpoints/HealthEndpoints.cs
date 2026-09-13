using Microsoft.AspNetCore.Diagnostics.HealthChecks;

namespace Shortener.Api.Endpoints;

/// <summary>
/// Kubernetes-style probes. Liveness never touches a dependency (a dead database must not
/// restart the pod); readiness checks everything tagged "ready".
/// </summary>
public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
    }
}
