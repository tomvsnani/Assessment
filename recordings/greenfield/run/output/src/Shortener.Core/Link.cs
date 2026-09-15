namespace Shortener.Core;

public record Link(string ShortCode, string LongUrl, long UsageCount = 0);
