using Shortener.Core.Links;
using Shortener.Infrastructure.InMemory;
using Shortener.UnitTests.Fakes;

namespace Shortener.UnitTests.Links;

public class LinkResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 13, 12, 0, 0, TimeSpan.Zero);
    private static readonly ShortCode Code = ShortCode.Parse("abc1234");

    private readonly InMemoryLinkRepository _repository = new();
    private readonly InMemoryLinkCache _cache = new();
    private readonly FakeClock _clock = new(Now);

    private LinkResolver Resolver => new(_repository, _cache, _clock);

    private async Task<Link> StoreAsync(DateTimeOffset? expiresAt = null)
    {
        var link = new Link(Code, new Uri("https://example.com"), Now, expiresAt, null);
        await _repository.TryAddAsync(link, CancellationToken.None);
        return link;
    }

    [Fact]
    public async Task Given_unknown_code_When_resolved_Then_returns_null()
    {
        (await Resolver.ResolveAsync(Code, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Given_cache_miss_When_resolved_Then_link_is_cached_and_click_recorded()
    {
        var link = await StoreAsync();

        var resolved = await Resolver.ResolveAsync(Code, CancellationToken.None);

        resolved.Should().Be(link);
        (await _cache.GetAsync(Code, CancellationToken.None)).Should().Be(link);
        var stats = await _repository.GetStatsAsync(Code, CancellationToken.None);
        stats!.TotalClicks.Should().Be(1);
        stats.LastClickedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Given_cache_hit_When_resolved_Then_repository_is_not_read_but_click_is_still_recorded()
    {
        var link = new Link(Code, new Uri("https://cached.example"), Now, null, null);
        await _cache.SetAsync(link, CancellationToken.None);
        await StoreAsync(); // repository holds a different target; cache must win

        var resolved = await Resolver.ResolveAsync(Code, CancellationToken.None);

        resolved!.TargetUrl.Host.Should().Be("cached.example");
        (await _repository.GetStatsAsync(Code, CancellationToken.None))!.TotalClicks.Should().Be(1);
    }

    [Fact]
    public async Task Given_expired_link_When_resolved_Then_returns_null_and_records_nothing()
    {
        await StoreAsync(expiresAt: Now.AddMinutes(-1));

        (await Resolver.ResolveAsync(Code, CancellationToken.None)).Should().BeNull();
        (await _repository.GetStatsAsync(Code, CancellationToken.None))!.TotalClicks.Should().Be(0);
    }

    [Fact]
    public async Task Given_link_expiring_later_When_clock_passes_expiry_Then_stops_resolving()
    {
        await StoreAsync(expiresAt: Now.AddMinutes(5));

        (await Resolver.ResolveAsync(Code, CancellationToken.None)).Should().NotBeNull();
        _clock.Advance(TimeSpan.FromMinutes(6));
        (await Resolver.ResolveAsync(Code, CancellationToken.None)).Should().BeNull();
    }
}
