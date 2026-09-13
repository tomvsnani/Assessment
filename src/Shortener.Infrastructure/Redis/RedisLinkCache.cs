using System.Text.Json;
using Shortener.Core.Links;
using Shortener.Core.Ports;
using StackExchange.Redis;

namespace Shortener.Infrastructure.Redis;

/// <summary>
/// Shared cache for multi-instance deployments. Entries carry a TTL so an expired link
/// disappears from the cache at the same moment it stops resolving.
/// A cache outage degrades to a repository read; it never fails a request.
/// </summary>
public sealed class RedisLinkCache(IConnectionMultiplexer redis, IClock clock) : ILinkCache
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromHours(24);

    public async Task<Link?> GetAsync(ShortCode code, CancellationToken ct)
    {
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(Key(code));
            return value.HasValue ? JsonSerializer.Deserialize<CachedLink>((string)value!)?.ToLink() : null;
        }
        catch (RedisException)
        {
            return null;
        }
    }

    public async Task SetAsync(Link link, CancellationToken ct)
    {
        var ttl = link.ExpiresAt is { } expiry ? expiry - clock.UtcNow : DefaultTtl;
        if (ttl <= TimeSpan.Zero)
        {
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(CachedLink.From(link));
            await redis.GetDatabase().StringSetAsync(Key(link.Code), json, ttl);
        }
        catch (RedisException)
        {
            // Best effort: the next request will populate it.
        }
    }

    private static string Key(ShortCode code) => $"link:{code.Value}";

    private sealed record CachedLink(string Code, string TargetUrl, DateTimeOffset CreatedAt, DateTimeOffset? ExpiresAt, string? IdempotencyKey)
    {
        public static CachedLink From(Link link) =>
            new(link.Code.Value, link.TargetUrl.ToString(), link.CreatedAt, link.ExpiresAt, link.IdempotencyKey);

        public Link ToLink() =>
            new(ShortCode.Parse(Code), new Uri(TargetUrl, UriKind.Absolute), CreatedAt, ExpiresAt, IdempotencyKey);
    }
}
