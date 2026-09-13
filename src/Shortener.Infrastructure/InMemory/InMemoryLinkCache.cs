using System.Collections.Concurrent;
using Shortener.Core.Links;
using Shortener.Core.Ports;

namespace Shortener.Infrastructure.InMemory;

/// <summary>Process-local cache. Unbounded on purpose: links are small and immutable.</summary>
public sealed class InMemoryLinkCache : ILinkCache
{
    private readonly ConcurrentDictionary<string, Link> _links = new(StringComparer.Ordinal);

    public Task<Link?> GetAsync(ShortCode code, CancellationToken ct) =>
        Task.FromResult(_links.GetValueOrDefault(code.Value));

    public Task SetAsync(Link link, CancellationToken ct)
    {
        _links[link.Code.Value] = link;
        return Task.CompletedTask;
    }
}
