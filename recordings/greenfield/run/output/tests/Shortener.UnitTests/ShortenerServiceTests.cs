using Shortener.Core;
using Shortener.Core.Ports;
using Shortener.Infrastructure;
using FluentAssertions;
using Moq;
using Xunit;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using System;

namespace Shortener.UnitTests;

public class ShortenerServiceTests
{
    private readonly Mock<ILinkRepository> _mockLinkRepository;
    private readonly Mock<IShortCodeGenerator> _mockShortCodeGenerator;
    private readonly Mock<ILogger<ShortenerService>> _mockLogger;
    private readonly ShortenerService _sut;

    public ShortenerServiceTests()
    {
        _mockLinkRepository = new Mock<ILinkRepository>();
        _mockShortCodeGenerator = new Mock<IShortCodeGenerator>();
        _mockLogger = new Mock<ILogger<ShortenerService>>();
        _sut = new ShortenerService(_mockLinkRepository.Object, _mockShortCodeGenerator.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task Given_NewLongUrl_When_CreateShortLink_Then_NewLinkIsCreatedAndReturned()
    {
        // Arrange
        var longUrl = "https://example.com/long/url/1";
        var shortCode = "abc12345";
        Link? savedLink = null; // To capture the link passed to SaveAsync

        _mockLinkRepository.Setup(r => r.FindByLongUrlAsync(longUrl)).ReturnsAsync((Link?)null);
        _mockShortCodeGenerator.Setup(g => g.GenerateCode()).Returns(shortCode);
        _mockLinkRepository.Setup(r => r.SaveAsync(It.IsAny<Link>()))
            .Callback<Link>(link => savedLink = link) // Capture the link
            .Returns(Task.CompletedTask);
        _mockLinkRepository.Setup(r => r.FindByShortCodeAsync(shortCode)).ReturnsAsync(() => savedLink); // Return the captured link

        // Act
        var (resultLink, isNew) = await _sut.CreateShortLinkAsync(longUrl);

        // Assert
        savedLink.Should().NotBeNull(); // Ensure something was saved
        resultLink.Should().BeEquivalentTo(savedLink);
        isNew.Should().BeTrue();
        _mockLinkRepository.Verify(r => r.FindByLongUrlAsync(longUrl), Times.Once);
        _mockShortCodeGenerator.Verify(g => g.GenerateCode(), Times.Once);
        _mockLinkRepository.Verify(r => r.SaveAsync(It.Is<Link>(l => l.ShortCode == shortCode && l.LongUrl == longUrl)), Times.Once);
    }

    [Fact]
    public async Task Given_ExistingLongUrl_When_CreateShortLink_Then_ExistingLinkIsReturned()
    {
        // Arrange
        var longUrl = "https://example.com/long/url/existing";
        var existingShortCode = "xyz98765";
        var existingLink = new Link(existingShortCode, longUrl, 10);

        _mockLinkRepository.Setup(r => r.FindByLongUrlAsync(longUrl)).ReturnsAsync(existingLink);

        // Act
        var (resultLink, isNew) = await _sut.CreateShortLinkAsync(longUrl);

        // Assert
        resultLink.Should().BeEquivalentTo(existingLink);
        isNew.Should().BeFalse();
        _mockLinkRepository.Verify(r => r.FindByLongUrlAsync(longUrl), Times.Once);
        _mockShortCodeGenerator.Verify(g => g.GenerateCode(), Times.Never);
        _mockLinkRepository.Verify(r => r.SaveAsync(It.IsAny<Link>()), Times.Never);
    }

    [Theory]
    [InlineData("invalid-url")]
    [InlineData("ftp://not-http.com")]
    [InlineData("")]
    public async Task Given_InvalidLongUrl_When_CreateShortLink_Then_ThrowsInvalidUrlException(string invalidUrl)
    {
        // Act
        Func<Task> action = async () => await _sut.CreateShortLinkAsync(invalidUrl);

        // Assert
        await action.Should().ThrowAsync<InvalidUrlException>();
        _mockLinkRepository.Verify(r => r.FindByLongUrlAsync(It.IsAny<string>()), Times.Never);
        _mockShortCodeGenerator.Verify(g => g.GenerateCode(), Times.Never);
        _mockLinkRepository.Verify(r => r.SaveAsync(It.IsAny<Link>()), Times.Never);
    }

    [Fact]
    public async Task Given_ShortCodeExists_When_RetrieveOriginalUrl_Then_ReturnsLongUrlAndIncrementsCount()
    {
        // Arrange
        var shortCode = "abc12345";
        var longUrl = "https://example.com/long/url/1";
        var existingLink = new Link(shortCode, longUrl, 0);

        _mockLinkRepository.Setup(r => r.FindByShortCodeAsync(shortCode)).ReturnsAsync(existingLink);
        _mockLinkRepository.Setup(r => r.IncrementUsageCountAsync(shortCode)).Returns(Task.CompletedTask);

        // Act
        var resultLongUrl = await _sut.RetrieveOriginalUrlAsync(shortCode);

        // Assert
        resultLongUrl.Should().Be(longUrl);
        _mockLinkRepository.Verify(r => r.FindByShortCodeAsync(shortCode), Times.Once);
        _mockLinkRepository.Verify(r => r.IncrementUsageCountAsync(shortCode), Times.Once);
    }

    [Fact]
    public async Task Given_ShortCodeDoesNotExist_When_RetrieveOriginalUrl_Then_ThrowsLinkNotFoundException()
    {
        // Arrange
        var nonExistentShortCode = "nonexistent";
        _mockLinkRepository.Setup(r => r.FindByShortCodeAsync(nonExistentShortCode)).ReturnsAsync((Link?)null);

        // Act
        Func<Task> action = async () => await _sut.RetrieveOriginalUrlAsync(nonExistentShortCode);

        // Assert
        await action.Should().ThrowAsync<LinkNotFoundException>();
        _mockLinkRepository.Verify(r => r.FindByShortCodeAsync(nonExistentShortCode), Times.Once);
        _mockLinkRepository.Verify(r => r.IncrementUsageCountAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Given_ShortCodeExists_When_GetUsageStats_Then_ReturnsUsageCount()
    {
        // Arrange
        var shortCode = "abc12345";
        var expectedUsageCount = 15L;
        var existingLink = new Link(shortCode, "https://example.com", expectedUsageCount);

        _mockLinkRepository.Setup(r => r.FindByShortCodeAsync(shortCode)).ReturnsAsync(existingLink);

        // Act
        var actualLink = await _sut.GetUsageStatsAsync(shortCode);

        // Assert
        actualLink.Should().BeEquivalentTo(existingLink); // Check if the entire Link object is returned correctly
        actualLink.UsageCount.Should().Be(expectedUsageCount);
        _mockLinkRepository.Verify(r => r.FindByShortCodeAsync(shortCode), Times.Once);
    }

    [Fact]
    public async Task Given_ShortCodeDoesNotExist_When_GetUsageStats_Then_ThrowsLinkNotFoundException()
    {
        // Arrange
        var nonExistentShortCode = "nonexistent";
        _mockLinkRepository.Setup(r => r.FindByShortCodeAsync(nonExistentShortCode)).ReturnsAsync((Link?)null);

        // Act
        Func<Task> action = async () => await _sut.GetUsageStatsAsync(nonExistentShortCode);

        // Assert
        await action.Should().ThrowAsync<LinkNotFoundException>();
        _mockLinkRepository.Verify(r => r.FindByShortCodeAsync(nonExistentShortCode), Times.Once);
    }
}
