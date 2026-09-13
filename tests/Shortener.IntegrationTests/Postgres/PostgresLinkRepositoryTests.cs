using Npgsql;
using Shortener.Core.Links;
using Shortener.Infrastructure.Postgres;
using Testcontainers.PostgreSql;

namespace Shortener.IntegrationTests.Postgres;

/// <summary>Real Postgres in a container. Same contract the in-memory repository satisfies.</summary>
public sealed class PostgresLinkRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private NpgsqlDataSource _dataSource = null!;
    private PostgresLinkRepository _repository = null!;

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("RUN_DOCKER_TESTS") != "1")
        {
            return;
        }

        await _container.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
        await PostgresSchema.EnsureCreatedAsync(_dataSource, CancellationToken.None);
        _repository = new PostgresLinkRepository(_dataSource, PostgresResilience.Build());
    }

    public async Task DisposeAsync()
    {
        if (_dataSource is not null)
        {
            await _dataSource.DisposeAsync();
        }

        await _container.DisposeAsync();
    }

    private static Link NewLink(string code, string? idempotencyKey = null) =>
        new(ShortCode.Parse(code), new Uri("https://example.com/x"), DateTimeOffset.UtcNow, null, idempotencyKey);

    [DockerFact]
    public async Task Given_new_code_When_added_Then_round_trips()
    {
        var link = NewLink("pgtest1");

        (await _repository.TryAddAsync(link, CancellationToken.None)).Should().BeTrue();
        var found = await _repository.FindAsync(link.Code, CancellationToken.None);

        found.Should().NotBeNull();
        found!.TargetUrl.Should().Be(link.TargetUrl);
        found.CreatedAt.Should().BeCloseTo(link.CreatedAt, TimeSpan.FromMilliseconds(1));
    }

    [DockerFact]
    public async Task Given_duplicate_code_When_added_Then_returns_false()
    {
        await _repository.TryAddAsync(NewLink("pgdup01"), CancellationToken.None);

        (await _repository.TryAddAsync(NewLink("pgdup01"), CancellationToken.None)).Should().BeFalse();
    }

    [DockerFact]
    public async Task Given_idempotency_key_When_looked_up_Then_original_is_returned()
    {
        var link = NewLink("pgidem1", "idem-key-1");
        await _repository.TryAddAsync(link, CancellationToken.None);

        var found = await _repository.FindByIdempotencyKeyAsync("idem-key-1", CancellationToken.None);

        found!.Code.Should().Be(link.Code);
    }

    [DockerFact]
    public async Task Given_clicks_recorded_When_stats_read_Then_count_matches()
    {
        var link = NewLink("pgclick");
        await _repository.TryAddAsync(link, CancellationToken.None);
        var at = DateTimeOffset.UtcNow;

        await _repository.RecordClickAsync(link.Code, at, CancellationToken.None);
        await _repository.RecordClickAsync(link.Code, at, CancellationToken.None);
        var stats = await _repository.GetStatsAsync(link.Code, CancellationToken.None);

        stats!.TotalClicks.Should().Be(2);
        stats.LastClickedAt.Should().BeCloseTo(at, TimeSpan.FromMilliseconds(1));
    }
}
