namespace Shortener.Api.Contracts;

/// <summary>Request body for POST /links. Mirrors docs/openapi.yaml.</summary>
public sealed record CreateLinkRequest(
    string Url,
    string? Alias = null,
    int? TtlSeconds = null);
