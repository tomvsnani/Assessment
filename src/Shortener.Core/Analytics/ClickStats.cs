namespace Shortener.Core.Analytics;

/// <summary>
/// v1 ("legacy") analytics: a single counter kept on the link row itself and updated synchronously
/// on every redirect. See the brownfield scenario for why this is being replaced.
/// </summary>
public sealed record ClickStats(long TotalClicks, DateTimeOffset? LastClickedAt);
