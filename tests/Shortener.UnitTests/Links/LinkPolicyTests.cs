using Shortener.Core.Links;

namespace Shortener.UnitTests.Links;

public class LinkPolicyTests
{
    private readonly LinkPolicy _policy = new();

    [Theory]
    [InlineData("https://example.com/path?q=1")]
    [InlineData("http://example.com")]
    public void Given_public_http_url_When_checked_Then_accepted(string url)
    {
        _policy.Reject(url).Should().BeNull();
    }

    [Theory]
    [InlineData("", "required")]
    [InlineData("not a url", "absolute")]
    [InlineData("ftp://example.com/file", "http")]
    [InlineData("javascript:alert(1)", "http")]
    [InlineData("http://localhost/admin", "host")]
    [InlineData("http://169.254.169.254/latest/meta-data", "host")]
    public void Given_disallowed_url_When_checked_Then_rejected_with_reason(string url, string reasonContains)
    {
        _policy.Reject(url).Should().ContainEquivalentOf(reasonContains);
    }

    [Fact]
    public void Given_url_over_max_length_When_checked_Then_rejected()
    {
        var url = "https://example.com/" + new string('a', LinkPolicy.MaxUrlLength);
        _policy.Reject(url).Should().Contain("exceeds");
    }

    [Fact]
    public void Given_extra_blocked_host_When_configured_Then_rejected()
    {
        var policy = new LinkPolicy(["internal.corp"]);
        policy.Reject("https://internal.corp/secret").Should().Contain("host");
    }
}
