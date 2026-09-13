using Shortener.Core.Links;
using Shortener.Core.Ports;

namespace Shortener.UnitTests.Fakes;

/// <summary>Returns the given codes in order, so collision handling can be tested deterministically.</summary>
public sealed class SequenceShortCodeGenerator(params string[] codes) : IShortCodeGenerator
{
    private readonly Queue<string> _codes = new(codes);

    public int Calls { get; private set; }

    public ShortCode Generate(int length)
    {
        Calls++;
        return ShortCode.Parse(_codes.Dequeue());
    }
}
