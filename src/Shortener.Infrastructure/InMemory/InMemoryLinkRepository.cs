using System.Collections.Concurrent;
using Shortener.Core.Analytics;
using Shortener.Core.Links;
using Shortener.Core.Ports;

namespace Shortener.Infrastructure.InMemory;

/// <summary>Default storage when no database is configured. Also used by the integration tests.</summary>
public sealed class InMemoryLinkRepository : ILinkRepository
{
    private sealed class Row(Link link)
    {
        public Link Link { get; } = link;
        public long Clicks;
        public DateTimeOffset? LastClickedAt;
    }

    private readonly ConcurrentDictionary<string, Row> _byCode = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Link> _byIdempotencyKey = new(StringComparer.Ordinal);

    public Task<bool> TryAddAsync(Link link, CancellationToken ct)
    {
        var added = _byCode.TryAdd(link.Code.Value, new Row(link));
        if (added && link.IdempotencyKey is { } key)
        {
            _byIdempotencyKey.TryAdd(key, link);
        }

        return Task.FromResult(added);
    }

    public Task<Link?> FindAsync(ShortCode code, CancellationToken ct) =>
        Task.FromResult(_byCode.TryGetValue(code.Value, out var row) ? row.Link : null);

    public Task<Link?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct) =>
        Task.FromResult(_byIdempotencyKey.GetValueOrDefault(idempotencyKey));

    public Task RecordClickAsync(ShortCode code, DateTimeOffset at, CancellationToken ct)
    {
        if (_byCode.TryGetValue(code.Value, out var row))
        {
            Interlocked.Increment(ref row.Clicks);
            row.LastClickedAt = at;
        }

        return Task.CompletedTask;
    }

    public Task<ClickStats?> GetStatsAsync(ShortCode code, CancellationToken ct) =>
        Task.FromResult(_byCode.TryGetValue(code.Value, out var row)
            ? new ClickStats(Interlocked.Read(ref row.Clicks), row.LastClickedAt)
            : null);
}
