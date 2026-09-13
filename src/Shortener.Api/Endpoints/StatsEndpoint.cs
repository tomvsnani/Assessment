using Shortener.Api.Contracts;
using Shortener.Core.Links;
using Shortener.Core.Ports;

namespace Shortener.Api.Endpoints;

public static class StatsEndpoint
{
    public static void MapStats(this IEndpointRouteBuilder app) =>
        app.MapGet("/links/{code}/stats", HandleAsync).WithName("GetStats");

    private static async Task<IResult> HandleAsync(string code, ILinkRepository repository, CancellationToken ct)
    {
        if (!ShortCode.TryParse(code, out var shortCode))
        {
            return Results.NotFound();
        }

        var stats = await repository.GetStatsAsync(shortCode, ct);
        return stats is null ? Results.NotFound() : Results.Ok(StatsResponse.From(shortCode.Value, stats));
    }
}
