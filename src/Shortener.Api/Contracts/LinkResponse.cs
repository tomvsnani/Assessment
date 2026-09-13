using Shortener.Core.Links;

namespace Shortener.Api.Contracts;

public sealed record LinkResponse(
    string Code,
    Uri ShortUrl,
    Uri TargetUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt)
{
    public static LinkResponse From(Link link, Uri baseUrl) =>
        new(link.Code.Value, new Uri(baseUrl, link.Code.Value), link.TargetUrl, link.CreatedAt, link.ExpiresAt);
}
