using Shortener.Core.Links;
using Shortener.Infrastructure.Codes;

namespace Shortener.UnitTests.Codes;

public class RandomShortCodeGeneratorTests
{
    private readonly RandomShortCodeGenerator _generator = new();

    [Fact]
    public void Given_length_When_generated_Then_code_has_that_length_and_is_valid()
    {
        var code = _generator.Generate(7);

        code.Value.Should().HaveLength(7);
        ShortCode.TryParse(code.Value, out _).Should().BeTrue();
    }

    [Fact]
    public void Given_many_codes_When_generated_Then_they_do_not_repeat()
    {
        var codes = Enumerable.Range(0, 10_000).Select(_ => _generator.Generate(7).Value).ToHashSet(StringComparer.Ordinal);

        codes.Should().HaveCount(10_000);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    public void Given_length_outside_bounds_When_generated_Then_throws(int length)
    {
        var act = () => _generator.Generate(length);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
