using Shortener.Core.Ports;

namespace Shortener.Core.Links;

/// <summary>
/// Redirect hot path: cache first, repository on miss, then record the click.
/// v1 records the click synchronously before returning — this is the latency problem the
/// brownfield scenario addresses.
/// </summary>
public sealed class LinkResolver(ILinkRepository repository, ILinkCache cache, IClock clock)
{
    public async Task<Link?> ResolveAsync(ShortCode code, CancellationToken ct)
    {
        var link = await cache.GetAsync(code, ct);
        if (link is null)
        {
            link = await repository.FindAsync(code, ct);
            if (link is null)
            {
                return null;
            }

            await cache.SetAsync(link, ct);
        }

        var now = clock.UtcNow;
        if (link.IsExpired(now))
        {
            return null;
        }

        await repository.RecordClickAsync(code, now, ct);
        return link;
    }
}
