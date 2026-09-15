namespace Shortener.Api.Models;

public record ShortLinkResponse(string ShortCode, string ShortUrl, long UsageCount = 0);
