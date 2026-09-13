using Shortener.Core.Links;

namespace Shortener.Api.Endpoints;

public static class RedirectEndpoint
{
    public static void MapRedirect(this IEndpointRouteBuilder app) =>
        app.MapGet("/{code}", HandleAsync).WithName("Redirect");

    private static async Task<IResult> HandleAsync(string code, LinkResolver resolver, CancellationToken ct)
    {
        if (!ShortCode.TryParse(code, out var shortCode))
        {
            return Results.NotFound();
        }

        var link = await resolver.ResolveAsync(shortCode, ct);
        if (link is null)
        {
            return Results.NotFound();
        }

        // 302 rather than 301: a permanent redirect would let browsers skip us entirely,
        // which breaks analytics and makes expiry/edits impossible to honour.
        return Results.Redirect(link.TargetUrl.ToString(), permanent: false);
    }
}
