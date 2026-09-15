using System.Collections.Concurrent;
using Shortener.Core;
using Shortener.Core.Ports;

namespace Shortener.Infrastructure;

public class InMemoryLinkRepository : ILinkRepository
{
    private readonly ConcurrentDictionary<string, long> _usageCounts = new();
    private readonly ConcurrentDictionary<string, Link> _linksByShortCode = new();
    private readonly ConcurrentDictionary<string, Link> _linksByLongUrl = new();

    public Task<Link?> FindByShortCodeAsync(string shortCode)
    {
        if (_linksByShortCode.TryGetValue(shortCode, out var link))
        {
            _usageCounts.TryGetValue(shortCode, out var usageCount);
            return Task.FromResult<Link?>(link with { UsageCount = usageCount });
        }
        return Task.FromResult<Link?>(null);
    }

    public Task<Link?> FindByLongUrlAsync(string longUrl)
    {
        if (_linksByLongUrl.TryGetValue(longUrl, out var link))
        {
            _usageCounts.TryGetValue(link.ShortCode, out var usageCount);
            return Task.FromResult<Link?>(link with { UsageCount = usageCount });
        }
        return Task.FromResult<Link?>(null);
    }

    public Task SaveAsync(Link link)
    {
        if (!_linksByShortCode.TryAdd(link.ShortCode, link))
        {
            throw new InvalidOperationException($"Short code '{link.ShortCode}' already exists.");
        }
        _linksByLongUrl.TryAdd(link.LongUrl, link);
        _usageCounts.TryAdd(link.ShortCode, link.UsageCount);
        return Task.CompletedTask;
    }

    public Task IncrementUsageCountAsync(string shortCode)
    {
        if (!_usageCounts.TryGetValue(shortCode, out _))
        {
            throw new LinkNotFoundException(shortCode);
        }

        _usageCounts.AddOrUpdate(
            shortCode,
            _ => throw new LinkNotFoundException(shortCode), // Should not happen if TryGetValue passes
            (_, count) => count + 1);
        return Task.CompletedTask;
    }

    public void Clear()
    {
        _linksByShortCode.Clear();
        _linksByLongUrl.Clear();
        _usageCounts.Clear();
    }
}
