using Shortener.Api.Models;
using Shortener.Core;
using Shortener.Core.Ports;
using Shortener.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Serilog;
using Microsoft.AspNetCore.Http.Extensions; // Required for GetDisplayUrl


var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for structured logging
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

builder.Services.AddSingleton<ILinkRepository, InMemoryLinkRepository>();
builder.Services.AddSingleton<IShortCodeGenerator, EightCharacterAlphanumericCodeGenerator>();
builder.Services.AddSingleton<ShortenerService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var exceptionHandlerPathFeature =
            context.Features.Get<IExceptionHandlerPathFeature>();

        if (exceptionHandlerPathFeature?.Error is InvalidUrlException invalidUrlEx)
        {
            app.Logger.LogInformation("Invalid URL submitted: {Message}", invalidUrlEx.Message);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7807",
                title = "Bad Request",
                status = StatusCodes.Status400BadRequest,
                detail = invalidUrlEx.Message
            });
            return;
        }

        if (exceptionHandlerPathFeature?.Error is LinkNotFoundException linkNotFoundEx)
        {
            app.Logger.LogInformation("Short code not found: {Message}", linkNotFoundEx.Message);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "https://tools.ietf.org/html/rfc7807",
                title = "Not Found",
                status = StatusCodes.Status404NotFound,
                detail = linkNotFoundEx.Message
            });
            return;
        }
        
        app.Logger.LogError(exceptionHandlerPathFeature?.Error, "An unhandled exception occurred: {Path}", exceptionHandlerPathFeature?.Path);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://tools.ietf.org/html/rfc7807",
            title = "Internal Server Error",
            status = StatusCodes.Status500InternalServerError,
            detail = "An unexpected error occurred."
        });
    });
});

app.UseHttpsRedirection();

// Health Check Endpoint (AC-7)
app.MapHealthChecks("/health");

// POST /links (AC-1, AC-6, AC-8)
app.MapPost("/links", async (CreateShortLinkRequest request, ShortenerService service, HttpContext httpContext) =>
{
    var (link, isNew) = await service.CreateShortLinkAsync(request.Url);
    
    // Construct the short URL based on the current request's host
    var shortUrl = UriHelper.BuildAbsolute(httpContext.Request.Scheme, httpContext.Request.Host, path: $"/{link.ShortCode}");

    var response = new ShortLinkResponse(link.ShortCode, shortUrl, link.UsageCount);

    if (isNew)
    {
        app.Logger.LogInformation("Created new short link for {LongUrl} with code {ShortCode}", link.LongUrl, link.ShortCode);
        return Results.Created(shortUrl, response);
    }
    else
    {
        app.Logger.LogInformation("Returned existing short link for {LongUrl} with code {ShortCode}", link.LongUrl, link.ShortCode);
        return Results.Ok(response); // Per D002
    }
})
.WithName("CreateShortLink")
.WithOpenApi();

// GET /{code} (AC-2, AC-3)
app.MapGet("/{code}", async (string code, ShortenerService service, HttpContext httpContext) =>
{
    var longUrl = await service.RetrieveOriginalUrlAsync(code);
    app.Logger.LogInformation("Redirecting short code {ShortCode} to {LongUrl}", code, longUrl);
    return Results.Redirect(longUrl, permanent: false);
})
.WithName("RedirectShortLink")
.WithOpenApi();

// GET /links/{code}/stats (AC-4, AC-5)
app.MapGet("/links/{code}/stats", async (string code, ShortenerService service, HttpContext httpContext) =>
{
    var link = await service.GetUsageStatsAsync(code);
    var shortUrl = UriHelper.BuildAbsolute(httpContext.Request.Scheme, httpContext.Request.Host, path: $"/{link.ShortCode}");
    app.Logger.LogInformation("Retrieved stats for short code {ShortCode}: UsageCount = {UsageCount}", code, link.UsageCount);
    return Results.Ok(new ShortLinkResponse(link.ShortCode, shortUrl, link.UsageCount));
})
.WithName("GetLinkStats")
.WithOpenApi();

app.Run();

public partial class Program { } // Moved to the very end
