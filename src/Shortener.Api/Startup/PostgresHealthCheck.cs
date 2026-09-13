using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Shortener.Api.Startup;

/// <summary>Readiness: can we open a connection and run a trivial query within a short timeout?</summary>
public sealed class PostgresHealthCheck(NpgsqlDataSource dataSource) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));

            await using var connection = await dataSource.OpenConnectionAsync(timeout.Token);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync(timeout.Token);
            return HealthCheckResult.Healthy();
        }
        catch (Exception e) when (e is NpgsqlException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Postgres unreachable", e);
        }
    }
}
