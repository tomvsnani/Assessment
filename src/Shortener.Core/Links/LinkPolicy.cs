namespace Shortener.Core.Links;

/// <summary>
/// Business rules for which URLs may be shortened. Kept separate from the handler so the rules
/// can be read (and changed) in one place. Blocked hosts are injectable so operations can extend
/// the list from configuration without a code change.
/// </summary>
public sealed class LinkPolicy
{
    public const int MaxUrlLength = 2048;

    private static readonly string[] AllowedSchemes = [Uri.UriSchemeHttp, Uri.UriSchemeHttps];

    // Prevents the service being used to probe internal networks (SSRF-style abuse).
    public static readonly IReadOnlyList<string> DefaultBlockedHosts =
        ["localhost", "127.0.0.1", "0.0.0.0", "::1", "169.254.169.254"];

    private readonly HashSet<string> _blockedHosts;

    public LinkPolicy(IEnumerable<string>? extraBlockedHosts = null)
    {
        _blockedHosts = new HashSet<string>(DefaultBlockedHosts, StringComparer.OrdinalIgnoreCase);
        _blockedHosts.UnionWith(extraBlockedHosts ?? []);
    }

    /// <summary>Returns a human-readable reason when the URL is rejected, or null when it is acceptable.</summary>
    public string? Reject(string rawUrl)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
        {
            return "URL is required.";
        }

        if (rawUrl.Length > MaxUrlLength)
        {
            return $"URL exceeds {MaxUrlLength} characters.";
        }

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return "URL must be absolute.";
        }

        if (!AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase))
        {
            return "Only http and https URLs are allowed.";
        }

        if (_blockedHosts.Contains(uri.Host))
        {
            return "URL host is not allowed.";
        }

        return null;
    }
}
