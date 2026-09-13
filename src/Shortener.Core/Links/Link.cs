namespace Shortener.Core.Links;

/// <summary>A stored short link. Immutable; a new record is created for any change.</summary>
public sealed record Link(
    ShortCode Code,
    Uri TargetUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt,
    string? IdempotencyKey)
{
    public bool IsExpired(DateTimeOffset now) => ExpiresAt is { } expiry && expiry <= now;
}
