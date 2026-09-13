using System.Security.Cryptography;
using Shortener.Core.Links;
using Shortener.Core.Ports;

namespace Shortener.Infrastructure.Codes;

/// <summary>
/// Cryptographically random codes so they cannot be enumerated by guessing the next one.
/// Sequential/base62-of-id schemes are faster but leak volume and allow scraping.
/// </summary>
public sealed class RandomShortCodeGenerator : IShortCodeGenerator
{
    public ShortCode Generate(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, ShortCode.MinLength);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, ShortCode.MaxLength);

        return ShortCode.Parse(RandomNumberGenerator.GetString(ShortCode.Alphabet, length));
    }
}
