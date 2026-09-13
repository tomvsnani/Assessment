using Microsoft.Extensions.Options;
using Npgsql;
using Shortener.Core.Links;
using Shortener.Core.Ports;
using Shortener.Infrastructure.Codes;
using Shortener.Infrastructure.InMemory;
using Shortener.Infrastructure.Postgres;
using Shortener.Infrastructure.Redis;
using Shortener.Infrastructure.Time;
using StackExchange.Redis;

namespace Shortener.Api.Startup;

public static class ServiceRegistration
{
    public static WebApplicationBuilder AddShortenerServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        services.AddOptions<ShortenerOptions>()
            .Bind(builder.Configuration.GetSection(ShortenerOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Domain
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IShortCodeGenerator, RandomShortCodeGenerator>();
        services.AddSingleton(sp => new LinkPolicy(sp.GetRequiredService<IOptions<ShortenerOptions>>().Value.BlockedHosts));
        services.AddScoped<CreateLinkHandler>();
        services.AddScoped<LinkResolver>();

        // Storage: chosen once at startup from configuration.
        var options = builder.Configuration.GetSection(ShortenerOptions.SectionName).Get<ShortenerOptions>() ?? new();
        switch (options.Storage)
        {
            case StorageKind.Postgres:
                AddPostgres(builder);
                break;
            case StorageKind.InMemory:
            default:
                services.AddSingleton<ILinkRepository, InMemoryLinkRepository>();
                break;
        }

        AddCache(builder);
        services.AddHealthChecks();
        return builder;
    }

    private static void AddPostgres(WebApplicationBuilder builder)
    {
        var connectionString = builder.Configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings__Postgres is required when Shortener__Storage=Postgres.");

        builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        builder.Services.AddSingleton(PostgresResilience.Build());
        builder.Services.AddSingleton<ILinkRepository, PostgresLinkRepository>();
        builder.Services.AddHealthChecks().AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);
    }

    private static void AddCache(WebApplicationBuilder builder)
    {
        var redis = builder.Configuration.GetConnectionString("Redis");
        if (string.IsNullOrWhiteSpace(redis))
        {
            builder.Services.AddSingleton<ILinkCache, InMemoryLinkCache>();
            return;
        }

        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redis));
        builder.Services.AddSingleton<ILinkCache, RedisLinkCache>();
    }

    /// <summary>Creates tables when running against Postgres. No-op for in-memory storage.</summary>
    public static async Task InitialiseStorageAsync(this WebApplication app)
    {
        if (app.Services.GetService<NpgsqlDataSource>() is { } dataSource)
        {
            await PostgresSchema.EnsureCreatedAsync(dataSource, app.Lifetime.ApplicationStopping);
        }
    }
}
