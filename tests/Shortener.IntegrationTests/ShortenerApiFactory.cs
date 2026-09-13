using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Shortener.IntegrationTests;

/// <summary>Hosts the real API in-process with in-memory storage. No Docker, no network.</summary>
public sealed class ShortenerApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Shortener:Storage"] = "InMemory",
            ["Shortener:BaseUrl"] = "http://sho.rt",
            ["ConnectionStrings:Redis"] = "",
        }));
    }

    public HttpClient CreateNonRedirectingClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
}
