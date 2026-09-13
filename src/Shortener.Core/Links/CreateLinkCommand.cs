namespace Shortener.Core.Links;

/// <param name="TargetUrl">Raw URL as supplied by the caller; validated by <see cref="LinkPolicy"/>.</param>
/// <param name="CustomAlias">Optional caller-chosen code. Must be unused.</param>
/// <param name="TimeToLive">Optional expiry, relative to now.</param>
/// <param name="IdempotencyKey">Optional. Repeating a request with the same key returns the original link.</param>
public sealed record CreateLinkCommand(
    string TargetUrl,
    string? CustomAlias = null,
    TimeSpan? TimeToLive = null,
    string? IdempotencyKey = null);
