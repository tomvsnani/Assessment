using Shortener.Core;

namespace Shortener.Core.Ports;

public interface ILinkRepository
{
    Task<Link?> FindByShortCodeAsync(string shortCode);
    Task<Link?> FindByLongUrlAsync(string longUrl); // Per D002
    Task SaveAsync(Link link);
    Task IncrementUsageCountAsync(string shortCode);
}
