using Shortener.Infrastructure;
using FluentAssertions;
using Xunit;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Shortener.UnitTests;

public class EightCharacterAlphanumericCodeGeneratorTests
{
    private readonly EightCharacterAlphanumericCodeGenerator _sut;
    private const string ExpectedChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

    public EightCharacterAlphanumericCodeGeneratorTests()
    {
        _sut = new EightCharacterAlphanumericCodeGenerator();
    }

    [Fact]
    public void Given_Generator_When_GenerateCode_Then_ReturnsEightCharacterCode()
    {
        // Act
        var code = _sut.GenerateCode();

        // Assert
        code.Should().NotBeNullOrEmpty();
        code.Length.Should().Be(8); // Per D003
    }

    [Fact]
    public void Given_Generator_When_GenerateCode_Then_ReturnsAlphanumericCode()
    {
        // Act
        var code = _sut.GenerateCode();

        // Assert
        code.Should().MatchRegex("^[a-zA-Z0-9]{8}$"); // Per D003
    }

    [Fact]
    public void Given_Generator_When_GenerateMultipleCodes_Then_CodesAreUnique()
    {
        // Arrange
        var codes = new HashSet<string>();
        const int numberOfCodesToGenerate = 1000;

        // Act
        for (int i = 0; i < numberOfCodesToGenerate; i++)
        {
            codes.Add(_sut.GenerateCode());
        }

        // Assert
        codes.Count.Should().Be(numberOfCodesToGenerate);
    }

    [Fact]
    public void Given_Generator_When_GenerateMultipleCodes_Then_CodesAreNotGuessable()
    {
        // This is a probabilistic test for 'unguessability' through randomness.
        // A true cryptographic test is complex and outside the scope of a simple unit test.
        // We are checking for a reasonable distribution, indicating random selection.
        var charCounts = new Dictionary<char, int>();
        const int numberOfCodes = 1000;
        const int codeLength = 8;
        double expectedOccurrencesPerChar = (double)(numberOfCodes * codeLength) / ExpectedChars.Length; 
        const double tolerancePercentage = 0.5; // Allow for 50% deviation due to randomness with small sample size

        for (int i = 0; i < numberOfCodes; i++)
        {
            var code = _sut.GenerateCode();
            foreach (char c in code)
            {
                charCounts.TryAdd(c, 0);
                charCounts[c]++;
            }
        }

        // Assert that characters are used with some distribution
        charCounts.Should().NotBeEmpty();
        foreach (var count in charCounts.Values)
        {
            // Use Within for double comparison with a percentage tolerance
            ((double)count).Should().BeApproximately(expectedOccurrencesPerChar, expectedOccurrencesPerChar * tolerancePercentage);
        }

        // Ensure a good mix of characters (not just a few used repeatedly)
        charCounts.Keys.Count.Should().BeGreaterOrEqualTo((int)(ExpectedChars.Length * 0.8)); // At least 80% of possible characters used
    }
}
