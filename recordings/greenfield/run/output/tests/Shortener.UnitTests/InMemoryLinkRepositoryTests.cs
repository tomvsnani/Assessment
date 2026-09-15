using Shortener.Core;
using Shortener.Infrastructure;
using FluentAssertions;
using Xunit;
using System.Threading.Tasks;

namespace Shortener.UnitTests;

public class InMemoryLinkRepositoryTests
{
    private readonly InMemoryLinkRepository _sut;

    public InMemoryLinkRepositoryTests()
    {
        _sut = new InMemoryLinkRepository();
    }

    [Fact]
    public async Task Given_NewLink_When_SaveAsync_Then_LinkIsStoredAndCanBeFoundByShortCode()
    {
        // Arrange
        var link = new Link("testcode", "https://example.com/long/url", 0);

        // Act
        await _sut.SaveAsync(link);
        var foundLink = await _sut.FindByShortCodeAsync(link.ShortCode);

        // Assert
        foundLink.Should().BeEquivalentTo(link);
    }

    [Fact]
    public async Task Given_NewLink_When_SaveAsync_Then_LinkCanBeFoundByLongUrl() // Per D002
    {
        // Arrange
        var link = new Link("testcode2", "https://example.com/long/url/2", 0);

        // Act
        await _sut.SaveAsync(link);
        var foundLink = await _sut.FindByLongUrlAsync(link.LongUrl);

        // Assert
        foundLink.Should().BeEquivalentTo(link);
    }

    [Fact]
    public async Task Given_ExistingLink_When_FindByShortCodeAsync_Then_ReturnsLink()
    {
        // Arrange
        var link = new Link("findme", "https://example.com/findme", 5);
        await _sut.SaveAsync(link);

        // Act
        var foundLink = await _sut.FindByShortCodeAsync("findme");

        // Assert
        foundLink.Should().BeEquivalentTo(link);
    }

    [Fact]
    public async Task Given_NonExistentShortCode_When_FindByShortCodeAsync_Then_ReturnsNull()
    {
        // Act
        var foundLink = await _sut.FindByShortCodeAsync("nonexistent");

        // Assert
        foundLink.Should().BeNull();
    }

    [Fact]
    public async Task Given_ExistingLink_When_FindByLongUrlAsync_Then_ReturnsLink()
    {
        // Arrange
        var longUrl = "https://example.com/findmelong";
        var link = new Link("findmel", longUrl, 10);
        await _sut.SaveAsync(link);

        // Act
        var foundLink = await _sut.FindByLongUrlAsync(longUrl);

        // Assert
        foundLink.Should().BeEquivalentTo(link);
    }

    [Fact]
    public async Task Given_NonExistentLongUrl_When_FindByLongUrlAsync_Then_ReturnsNull()
    {
        // Act
        var foundLink = await _sut.FindByLongUrlAsync("https://example.com/nonexistent");

        // Assert
        foundLink.Should().BeNull();
    }

    [Fact]
    public async Task Given_ExistingLink_When_IncrementUsageCountAsync_Then_CountIsIncreased()
    {
        // Arrange
        var link = new Link("countme", "https://example.com/countme", 0);
        await _sut.SaveAsync(link);

        // Act
        await _sut.IncrementUsageCountAsync("countme");
        var updatedLink = await _sut.FindByShortCodeAsync("countme");

        // Assert
        updatedLink.Should().NotBeNull();
        updatedLink!.UsageCount.Should().Be(1);
    }

    [Fact]
    public async Task Given_MultipleIncrements_When_IncrementUsageCountAsync_Then_CountIsIncreasedCorrectly()
    {
        // Arrange
        var link = new Link("multicount", "https://example.com/multicount", 0);
        await _sut.SaveAsync(link);

        // Act
        await _sut.IncrementUsageCountAsync("multicount");
        await _sut.IncrementUsageCountAsync("multicount");
        await _sut.IncrementUsageCountAsync("multicount");
        var updatedLink = await _sut.FindByShortCodeAsync("multicount");

        // Assert
        updatedLink.Should().NotBeNull();
        updatedLink!.UsageCount.Should().Be(3);
    }

    [Fact]
    public async Task Given_NonExistentLink_When_IncrementUsageCountAsync_Then_ThrowsLinkNotFoundException()
    {
        // Act
        Func<Task> action = async () => await _sut.IncrementUsageCountAsync("nonexistent");

        // Assert
        await action.Should().ThrowAsync<LinkNotFoundException>(); // Changed from InvalidOperationException
    }

    [Fact]
    public async Task Given_DuplicateShortCode_When_SaveAsync_Then_ThrowsInvalidOperationException()
    {
        // Arrange
        var link1 = new Link("dupe", "https://example.com/dupe1", 0);
        var link2 = new Link("dupe", "https://example.com/dupe2", 0); // Same short code, different long URL
        await _sut.SaveAsync(link1);

        // Act
        Func<Task> action = async () => await _sut.SaveAsync(link2);

        // Assert
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"Short code '{link2.ShortCode}' already exists.");
    }
}
