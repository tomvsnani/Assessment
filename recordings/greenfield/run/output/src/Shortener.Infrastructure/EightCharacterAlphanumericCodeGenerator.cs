using System.Security.Cryptography;
using Shortener.Core.Ports;

namespace Shortener.Infrastructure;

public class EightCharacterAlphanumericCodeGenerator : IShortCodeGenerator
{
    private const string Chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
    private const int CodeLength = 8;

    public string GenerateCode()
    {
        Span<byte> data = stackalloc byte[CodeLength];
        using (var rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(data);
        }

        char[] result = new char[CodeLength];
        for (int i = 0; i < CodeLength; i++)
        {
            result[i] = Chars[data[i] % Chars.Length];
        }
        return new string(result);
    }
}
