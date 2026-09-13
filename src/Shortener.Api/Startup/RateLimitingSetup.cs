using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Shortener.Api.Startup;

/// <summary>
/// Link creation is rate limited per client IP. Redirects are not: they are the hot path and a
/// limiter there would cost more than it protects (abuse is handled upstream by the edge).
/// </summary>
public static class RateLimitingSetup
{
    public const string CreatePolicy = "create-link";

    public static WebApplicationBuilder AddRateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(CreatePolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 60,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    }));
        });
        return builder;
    }
}
