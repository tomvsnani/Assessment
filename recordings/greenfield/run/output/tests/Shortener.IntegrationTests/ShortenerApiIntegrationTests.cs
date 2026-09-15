using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Net.Http.Json;
using Xunit;
using Microsoft.AspNetCore.Mvc;
using Shortener.Api.Models;
using Shortener.Core;
using Shortener.Core.Ports;
using Shortener.Infrastructure;

namespace Shortener.IntegrationTests;

public class ShortenerApiIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private ILinkRepository? _linkRepository; // To hold the resolved repository instance

    public ShortenerApiIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Ensure we use the InMemoryLinkRepository for tests
                // The service registration remains AddSingleton, but Clear() will reset its state per test fixture
                services.AddSingleton<ILinkRepository, InMemoryLinkRepository>();
                services.AddSingleton<IShortCodeGenerator, EightCharacterAlphanumericCodeGenerator>();
                services.AddSingleton<ShortenerService>();
            });
            builder.UseUrls("http://localhost"); // Explicitly use HTTP for integration tests
        });
    }

    public async Task InitializeAsync()
    {
        // Resolve the InMemoryLinkRepository and clear its state before each test run
        _linkRepository = _factory.Services.GetRequiredService<ILinkRepository>();
        if (_linkRepository is InMemoryLinkRepository inMemoryRepo)
        {
            inMemoryRepo.Clear();
        }
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // No special cleanup needed after all tests in the fixture run
        await Task.CompletedTask;
    }

    [Fact]
    public async Task PostLinks_WithValidUrl_ReturnsCreatedAndShortLinkResponse()
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new CreateShortLinkRequest("https://www.google.com");

        // Act
        var response = await client.PostAsJsonAsync("/links", request);
        var content = await response.Content.ReadFromJsonAsync<ShortLinkResponse>();

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        content.Should().NotBeNull();
        content!.ShortCode.Should().NotBeNullOrEmpty();
        content.ShortUrl.Should().Contain(content.ShortCode);
        content.UsageCount.Should().Be(0);
    }

    [Fact]
    public async Task PostLinks_WithExistingUrl_ReturnsOkAndExistingShortLinkResponse() // AC-8, D002
    {
        // Arrange
        var client = _factory.CreateClient();
        var longUrl = "https://www.microsoft.com";
        var request = new CreateShortLinkRequest(longUrl);

        // First, create the link
        var firstResponse = await client.PostAsJsonAsync("/links", request);
        firstResponse.EnsureSuccessStatusCode();
        var firstContent = await firstResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();

        // Act: Post the same URL again
        var secondResponse = await client.PostAsJsonAsync("/links", request);
        var secondContent = await secondResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();

        // Assert
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        secondContent.Should().NotBeNull();
        secondContent!.ShortCode.Should().Be(firstContent!.ShortCode);
        secondContent.ShortUrl.Should().Be(firstContent.ShortUrl);
    }

    [Theory]
    [InlineData("invalid-url")]
    [InlineData("ftp://not-http.com")]
    [InlineData("")]
    public async Task PostLinks_WithInvalidUrl_ReturnsBadRequest(string invalidUrl) // AC-6
    {
        // Arrange
        var client = _factory.CreateClient();
        var request = new CreateShortLinkRequest(invalidUrl);

        // Act
        var response = await client.PostAsJsonAsync("/links", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problemDetails = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Title.Should().Be("Bad Request");
    }

    [Fact]
    public async Task GetShortCode_WithValidCode_RedirectsToOriginalUrlAndIncrementsCount() // AC-2
    {
        // Arrange
        var client = _factory.CreateClient();
        var longUrl = "https://www.apple.com";
        var postRequest = new CreateShortLinkRequest(longUrl);
        var postResponse = await client.PostAsJsonAsync("/links", postRequest);
        var postContent = await postResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();
        var shortCode = postContent!.ShortCode;

        // Create a client that does NOT follow redirects automatically
        var redirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Act
        var getResponse = await redirectClient.GetAsync($"/{shortCode}");

        // Assert
        getResponse.StatusCode.Should().Be(HttpStatusCode.Found); // 302 Found
        getResponse.Headers.Location.Should().Be(longUrl);

        // Verify usage count incremented (AC-2)
        var statsResponse = await client.GetAsync($"/links/{shortCode}/stats");
        statsResponse.EnsureSuccessStatusCode();
        var statsContent = await statsResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();
        statsContent!.UsageCount.Should().Be(1);
    }

    [Fact]
    public async Task GetShortCode_WithNonExistentCode_ReturnsNotFound() // AC-3
    {
        // Arrange
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        // Act
        var response = await client.GetAsync("/nonexistentcode");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problemDetails = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Title.Should().Be("Not Found");
    }

    [Fact]
    public async Task GetLinkStats_WithValidCode_ReturnsUsageCount() // AC-4
    {
        // Arrange
        var client = _factory.CreateClient();
        var longUrl = "https://www.amazon.com";
        var postRequest = new CreateShortLinkRequest(longUrl);
        var postResponse = await client.PostAsJsonAsync("/links", postRequest);
        var postContent = await postResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();
        var shortCode = postContent!.ShortCode;

        // Increment usage count once
        var redirectClient = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await redirectClient.GetAsync($"/{shortCode}");

        // Act
        var statsResponse = await client.GetAsync($"/links/{shortCode}/stats");
        var statsContent = await statsResponse.Content.ReadFromJsonAsync<ShortLinkResponse>();

        // Assert
        statsResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        statsContent.Should().NotBeNull();
        statsContent!.ShortCode.Should().Be(shortCode);
        statsContent.UsageCount.Should().Be(1);
    }

    [Fact]
    public async Task GetLinkStats_WithNonExistentCode_ReturnsNotFound() // AC-5
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/links/nonexistentstats/stats");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problemDetails = await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        problemDetails.Should().NotBeNull();
        problemDetails!.Title.Should().Be("Not Found");
    }

    [Fact]
    public async Task GetHealth_ReturnsOk() // AC-7
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
