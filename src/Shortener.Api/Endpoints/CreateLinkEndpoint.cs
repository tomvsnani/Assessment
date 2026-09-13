using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shortener.Api.Contracts;
using Shortener.Api.Startup;
using Shortener.Core.Links;

namespace Shortener.Api.Endpoints;

public static class CreateLinkEndpoint
{
    public const string IdempotencyHeader = "Idempotency-Key";

    public static void MapCreateLink(this IEndpointRouteBuilder app) =>
        app.MapPost("/links", HandleAsync)
            .RequireRateLimiting(RateLimitingSetup.CreatePolicy)
            .WithName("CreateLink");

    private static async Task<IResult> HandleAsync(
        CreateLinkRequest request,
        [FromHeader(Name = IdempotencyHeader)] string? idempotencyKey,
        CreateLinkHandler handler,
        IOptions<ShortenerOptions> options,
        ILogger<CreateLinkHandler> logger,
        CancellationToken ct)
    {
        var command = new CreateLinkCommand(
            request.Url,
            request.Alias,
            request.TtlSeconds is { } ttl ? TimeSpan.FromSeconds(ttl) : null,
            idempotencyKey);

        var result = await handler.HandleAsync(command, ct);

        switch (result)
        {
            case CreateLinkResult.Created created:
                // Log the code and target host only; the full URL may contain personal data.
                logger.LogInformation("Link {Code} -> {TargetHost} ({Outcome})",
                    created.Link.Code, created.Link.TargetUrl.Host, created.WasExisting ? "existing" : "created");
                var body = LinkResponse.From(created.Link, options.Value.BaseUrl);
                return created.WasExisting ? Results.Ok(body) : Results.Created(body.ShortUrl, body);

            case CreateLinkResult.RejectedUrl rejected:
                return Problem(StatusCodes.Status400BadRequest, "Invalid URL", rejected.Reason);
            case CreateLinkResult.InvalidAlias invalid:
                return Problem(StatusCodes.Status400BadRequest, "Invalid alias", invalid.Reason);
            case CreateLinkResult.AliasTaken taken:
                return Problem(StatusCodes.Status409Conflict, "Alias taken", $"'{taken.Alias}' is already in use.");
            case CreateLinkResult.CodeCollision:
                logger.LogWarning("Short code generation collided repeatedly");
                return Problem(StatusCodes.Status503ServiceUnavailable, "Try again", "Could not allocate a code.");
            default:
                throw new InvalidOperationException($"Unhandled result {result.GetType().Name}");
        }
    }

    private static IResult Problem(int status, string title, string detail) =>
        Results.Problem(statusCode: status, title: title, detail: detail);
}
