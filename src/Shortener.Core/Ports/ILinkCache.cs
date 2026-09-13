using Shortener.Core.Links;

namespace Shortener.Core.Ports;

/// <summary>Read-through cache in front of <see cref="ILinkRepository"/> for the redirect hot path.</summary>
public interface ILinkCache
{
    Task<Link?> GetAsync(ShortCode code, CancellationToken ct);

    Task SetAsync(Link link, CancellationToken ct);
}
