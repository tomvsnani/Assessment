using Shortener.Core.Links;

namespace Shortener.UnitTests.Links;

public class ShortCodeTests
{
    [Theory]
    [InlineData("abcd")]
    [InlineData("Ab3xYz9")]
    [InlineData("0123456789abcdef")]
    public void Given_valid_base62_When_parsed_Then_succeeds(string value)
    {
        ShortCode.TryParse(value, out var code).Should().BeTrue();
        code.Value.Should().Be(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("0123456789abcdefg")]
    [InlineData("ab-cd")]
    [InlineData("ab cd")]
    [InlineData("äbcd")]
    public void Given_invalid_value_When_parsed_Then_fails(string? value)
    {
        ShortCode.TryParse(value, out _).Should().BeFalse();
    }

    [Fact]
    public void Given_invalid_value_When_Parse_Then_throws()
    {
        var act = () => ShortCode.Parse("no!");
        act.Should().Throw<ArgumentException>();
    }
}
