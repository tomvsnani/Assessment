using Dapper;
using Npgsql;

namespace Shortener.Infrastructure.Postgres;

/// <summary>
/// v1 schema. Applied idempotently at startup; a real deployment would use a migration tool,
/// but one table does not justify one yet (see docs/tradeoffs.md).
/// </summary>
public static class PostgresSchema
{
    public const string CreateTables = """
        CREATE TABLE IF NOT EXISTS links (
            code             TEXT        PRIMARY KEY,
            target_url       TEXT        NOT NULL,
            created_at       TIMESTAMPTZ NOT NULL,
            expires_at       TIMESTAMPTZ NULL,
            idempotency_key  TEXT        NULL UNIQUE,
            clicks           BIGINT      NOT NULL DEFAULT 0,
            last_clicked_at  TIMESTAMPTZ NULL
        );
        """;

    public static async Task EnsureCreatedAsync(NpgsqlDataSource dataSource, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await connection.ExecuteAsync(new CommandDefinition(CreateTables, cancellationToken: ct));
    }
}
