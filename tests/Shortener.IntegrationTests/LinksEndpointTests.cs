using System.Net;
using System.Net.Http.Json;
using Shortener.Api.Contracts;

namespace Shortener.IntegrationTests;

public class LinksEndpointTests : IClassFixture<ShortenerApiFactory>
{
    private readonly HttpClient _client;

    public LinksEndpointTests(ShortenerApiFactory factory) => _client = factory.CreateNonRedirectingClient();

    private async Task<LinkResponse> CreateAsync(string url, string? alias = null)
    {
        var response = await _client.PostAsJsonAsync("/links", new CreateLinkRequest(url, alias));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<LinkResponse>())!;
    }

    [Fact]
    public async Task Given_valid_url_When_posted_Then_201_with_short_url_and_location()
    {
        var response = await _client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/page"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<LinkResponse>();
        body!.ShortUrl.ToString().Should().Be($"http://sho.rt/{body.Code}");
        response.Headers.Location.Should().Be(body.ShortUrl);
    }

    [Fact]
    public async Task Given_created_link_When_short_code_requested_Then_302_to_target()
    {
        var link = await CreateAsync("https://example.com/target");

        var response = await _client.GetAsync($"/{link.Code}");

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location.Should().Be(new Uri("https://example.com/target"));
    }

    [Fact]
    public async Task Given_two_redirects_When_stats_requested_Then_counts_two_clicks()
    {
        var link = await CreateAsync("https://example.com/counted");
        await _client.GetAsync($"/{link.Code}");
        await _client.GetAsync($"/{link.Code}");

        var stats = await _client.GetFromJsonAsync<StatsResponse>($"/links/{link.Code}/stats");

        stats!.TotalClicks.Should().Be(2);
        stats.LastClickedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Given_unknown_code_When_requested_Then_404()
    {
        (await _client.GetAsync("/nope404")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _client.GetAsync("/links/nope404/stats")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Given_private_host_When_posted_Then_400_problem_details()
    {
        var response = await _client.PostAsJsonAsync("/links", new CreateLinkRequest("http://localhost/admin"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("host is not allowed");
    }

    [Fact]
    public async Task Given_alias_in_use_When_posted_again_Then_409()
    {
        await CreateAsync("https://a.example", alias: "sameAlias");

        var response = await _client.PostAsJsonAsync("/links", new CreateLinkRequest("https://b.example", "sameAlias"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Given_same_idempotency_key_When_posted_twice_Then_second_is_200_with_same_code()
    {
        using var first = new HttpRequestMessage(HttpMethod.Post, "/links") { Content = JsonContent.Create(new CreateLinkRequest("https://idem.example")) };
        first.Headers.Add("Idempotency-Key", "key-123");
        using var second = new HttpRequestMessage(HttpMethod.Post, "/links") { Content = JsonContent.Create(new CreateLinkRequest("https://idem.example")) };
        second.Headers.Add("Idempotency-Key", "key-123");

        var firstResponse = await _client.SendAsync(first);
        var secondResponse = await _client.SendAsync(second);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var a = await firstResponse.Content.ReadFromJsonAsync<LinkResponse>();
        var b = await secondResponse.Content.ReadFromJsonAsync<LinkResponse>();
        b!.Code.Should().Be(a!.Code);
    }

    [Fact]
    public async Task Given_incoming_correlation_id_When_any_request_Then_it_is_echoed()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "corr-42");

        var response = await _client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle().Which.Should().Be("corr-42");
    }

    [Fact]
    public async Task Given_no_correlation_id_When_any_request_Then_one_is_minted()
    {
        var response = await _client.GetAsync("/health/live");

        response.Headers.GetValues("X-Correlation-Id").Should().ContainSingle().Which.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Given_in_memory_storage_When_probes_hit_Then_both_healthy()
    {
        (await _client.GetAsync("/health/live")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _client.GetAsync("/health/ready")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
