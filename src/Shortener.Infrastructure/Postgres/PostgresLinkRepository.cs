using Dapper;
using Npgsql;
using Polly;
using Shortener.Core.Analytics;
using Shortener.Core.Links;
using Shortener.Core.Ports;

namespace Shortener.Infrastructure.Postgres;

public sealed class PostgresLinkRepository(NpgsqlDataSource dataSource, ResiliencePipeline resilience) : ILinkRepository
{
    private const string UniqueViolation = "23505";

    public Task<bool> TryAddAsync(Link link, CancellationToken ct) =>
        resilience.ExecuteAsync(async token =>
        {
            const string sql = """
                INSERT INTO links (code, target_url, created_at, expires_at, idempotency_key)
                VALUES (@Code, @TargetUrl, @CreatedAt, @ExpiresAt, @IdempotencyKey)
                ON CONFLICT (code) DO NOTHING
                """;
            await using var connection = await dataSource.OpenConnectionAsync(token);
            try
            {
                var rows = await connection.ExecuteAsync(new CommandDefinition(sql, new
                {
                    Code = link.Code.Value,
                    TargetUrl = link.TargetUrl.ToString(),
                    link.CreatedAt,
                    link.ExpiresAt,
                    link.IdempotencyKey,
                }, cancellationToken: token));
                return rows == 1;
            }
            catch (PostgresException e) when (e.SqlState == UniqueViolation)
            {
                return false; // idempotency_key already used by a concurrent request
            }
        }, ct).AsTask();

    public Task<Link?> FindAsync(ShortCode code, CancellationToken ct) =>
        resilience.ExecuteAsync(async token =>
        {
            const string sql = "SELECT code AS Code, target_url AS TargetUrl, created_at AS CreatedAt, expires_at AS ExpiresAt, idempotency_key AS IdempotencyKey FROM links WHERE code = @Code";
            await using var connection = await dataSource.OpenConnectionAsync(token);
            var row = await connection.QuerySingleOrDefaultAsync<LinkRow>(
                new CommandDefinition(sql, new { Code = code.Value }, cancellationToken: token));
            return row?.ToLink();
        }, ct).AsTask();

    public Task<Link?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct) =>
        resilience.ExecuteAsync(async token =>
        {
            const string sql = "SELECT code AS Code, target_url AS TargetUrl, created_at AS CreatedAt, expires_at AS ExpiresAt, idempotency_key AS IdempotencyKey FROM links WHERE idempotency_key = @Key";
            await using var connection = await dataSource.OpenConnectionAsync(token);
            var row = await connection.QuerySingleOrDefaultAsync<LinkRow>(
                new CommandDefinition(sql, new { Key = idempotencyKey }, cancellationToken: token));
            return row?.ToLink();
        }, ct).AsTask();

    public Task RecordClickAsync(ShortCode code, DateTimeOffset at, CancellationToken ct) =>
        resilience.ExecuteAsync(async token =>
        {
            // v1: a row-level UPDATE on every redirect. Under load this serialises on hot codes.
            const string sql = "UPDATE links SET clicks = clicks + 1, last_clicked_at = @At WHERE code = @Code";
            await using var connection = await dataSource.OpenConnectionAsync(token);
            await connection.ExecuteAsync(new CommandDefinition(sql, new { Code = code.Value, At = at }, cancellationToken: token));
        }, ct).AsTask();

    public Task<ClickStats?> GetStatsAsync(ShortCode code, CancellationToken ct) =>
        resilience.ExecuteAsync(async token =>
        {
            const string sql = "SELECT clicks AS Clicks, last_clicked_at AS LastClickedAt FROM links WHERE code = @Code";
            await using var connection = await dataSource.OpenConnectionAsync(token);
            var row = await connection.QuerySingleOrDefaultAsync<StatsRow>(
                new CommandDefinition(sql, new { Code = code.Value }, cancellationToken: token));
            return row is null ? null : new ClickStats(row.Clicks, row.LastClickedAtOffset);
        }, ct).AsTask();

    // Dapper materialises into these via the constructor, so SELECTs alias snake_case columns to these names.
    // Kept private so the column shape never leaks out of this file.
    private sealed record LinkRow(string Code, string TargetUrl, DateTime CreatedAt, DateTime? ExpiresAt, string? IdempotencyKey)
    {
        public Link ToLink() => new(
            ShortCode.Parse(Code),
            new Uri(TargetUrl, UriKind.Absolute),
            new DateTimeOffset(CreatedAt, TimeSpan.Zero),
            ExpiresAt is { } e ? new DateTimeOffset(e, TimeSpan.Zero) : null,
            IdempotencyKey);
    }

    private sealed record StatsRow(long Clicks, DateTime? LastClickedAt)
    {
        public DateTimeOffset? LastClickedAtOffset => LastClickedAt is { } t ? new DateTimeOffset(t, TimeSpan.Zero) : null;
    }
}
