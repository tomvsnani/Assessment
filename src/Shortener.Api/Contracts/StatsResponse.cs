using Shortener.Core.Analytics;

namespace Shortener.Api.Contracts;

public sealed record StatsResponse(string Code, long TotalClicks, DateTimeOffset? LastClickedAt)
{
    public static StatsResponse From(string code, ClickStats stats) =>
        new(code, stats.TotalClicks, stats.LastClickedAt);
}
