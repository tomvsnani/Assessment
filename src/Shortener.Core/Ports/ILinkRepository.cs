using Shortener.Core.Analytics;
using Shortener.Core.Links;

namespace Shortener.Core.Ports;

public interface ILinkRepository
{
    /// <summary>Returns false if the code is already taken. Must be atomic.</summary>
    Task<bool> TryAddAsync(Link link, CancellationToken ct);

    Task<Link?> FindAsync(ShortCode code, CancellationToken ct);

    Task<Link?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken ct);

    /// <summary>v1: increments the click counter on the link row. Called on the redirect path.</summary>
    Task RecordClickAsync(ShortCode code, DateTimeOffset at, CancellationToken ct);

    Task<ClickStats?> GetStatsAsync(ShortCode code, CancellationToken ct);
}
