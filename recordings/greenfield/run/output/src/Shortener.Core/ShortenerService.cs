using Shortener.Core.Ports;
using Microsoft.Extensions.Logging;

namespace Shortener.Core;

public class ShortenerService
{
    private readonly ILinkRepository _linkRepository;
    private readonly IShortCodeGenerator _shortCodeGenerator;
    private readonly ILogger<ShortenerService> _logger;

    public ShortenerService(ILinkRepository linkRepository, IShortCodeGenerator shortCodeGenerator, ILogger<ShortenerService> logger)
    {
        _linkRepository = linkRepository;
        _shortCodeGenerator = shortCodeGenerator;
        _logger = logger;
    }

    public async Task<(Link Link, bool IsNew)> CreateShortLinkAsync(string longUrl)
    {
        if (!Uri.TryCreate(longUrl, UriKind.Absolute, out var uriResult) || !(uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps))
        {
            _logger.LogWarning("Attempted to shorten an invalid URL: {LongUrl}", longUrl);
            throw new InvalidUrlException(longUrl);
        }

        // Per D002: Check if long URL already exists
        var existingLink = await _linkRepository.FindByLongUrlAsync(longUrl);
        if (existingLink is not null)
        {
            _logger.LogInformation("Long URL {LongUrl} already shortened to {ShortCode}", longUrl, existingLink.ShortCode);
            return (existingLink, false);
        }

        string shortCode;
        Link? newLink = null;
        bool isUnique = false;
        do
        {
            shortCode = _shortCodeGenerator.GenerateCode();
            var linkByCode = await _linkRepository.FindByShortCodeAsync(shortCode);
            if (linkByCode is null)
            {
                newLink = new Link(shortCode, longUrl);
                try
                {
                    await _linkRepository.SaveAsync(newLink);
                    isUnique = true;
                    _logger.LogInformation("Created new short link: {ShortCode} for {LongUrl}", shortCode, longUrl);
                }
                catch (Exception ex)
                {
                    // In case of a rare race condition where another request saves the same code
                    _logger.LogWarning(ex, "Race condition saving short code {ShortCode}", shortCode);
                }
            }
        } while (!isUnique);

        return (newLink!, true);
    }

    public async Task<string> RetrieveOriginalUrlAsync(string shortCode)
    {
        var link = await _linkRepository.FindByShortCodeAsync(shortCode);
        if (link is null)
        {
            _logger.LogWarning("Short code {ShortCode} not found for retrieval", shortCode);
            throw new LinkNotFoundException(shortCode);
        }
        await _linkRepository.IncrementUsageCountAsync(shortCode);
        _logger.LogInformation("Redirecting {ShortCode} to {LongUrl}", shortCode, link.LongUrl);
        return link.LongUrl;
    }

    public async Task<Link> GetUsageStatsAsync(string shortCode)
    {
        var link = await _linkRepository.FindByShortCodeAsync(shortCode);
        if (link is null)
        {
            _logger.LogWarning("Short code {ShortCode} not found for stats", shortCode);
            throw new LinkNotFoundException(shortCode);
        }
        _logger.LogInformation("Retrieved stats for {ShortCode}: {UsageCount}", shortCode, link.UsageCount);
        return link;
    }
}
