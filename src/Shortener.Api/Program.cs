using Serilog;
using Shortener.Api.Endpoints;
using Shortener.Api.Middleware;
using Shortener.Api.Startup;

// Composition root. Every "how" lives in a named class under Startup/, Middleware/ or Endpoints/.
var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.AddShortenerServices();
builder.AddRateLimiting();

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseRateLimiter();

app.MapHealthEndpoints();
app.MapCreateLink();
app.MapStats();
app.MapRedirect(); // last: it owns the catch-all "/{code}" route

await app.InitialiseStorageAsync();
await app.RunAsync();

/// <summary>Exposed so integration tests can host the app with <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
