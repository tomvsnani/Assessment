namespace Shortener.Api.Startup;

/// <summary>
/// Bound from the "Shortener" configuration section. Twelve-Factor: every value can be supplied
/// as an environment variable, e.g. <c>Shortener__Storage=Postgres</c>.
/// </summary>
public sealed class ShortenerOptions
{
    public const string SectionName = "Shortener";

    /// <summary>Public origin used to build short URLs in responses, e.g. https://sho.rt</summary>
    public Uri BaseUrl { get; set; } = new("http://localhost:8080");

    public StorageKind Storage { get; set; } = StorageKind.InMemory;

    /// <summary>Extra hosts to refuse in addition to the built-in loopback/metadata list.</summary>
    public IList<string> BlockedHosts { get; } = [];
}

public enum StorageKind
{
    InMemory,
    Postgres,
}
