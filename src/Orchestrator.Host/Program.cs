using Orchestrator.Host;
using Orchestrator.Host.Endpoints;
using Orchestrator.Host.Headless;
using Orchestrator.Host.Runs;
using Serilog;
using Serilog.Formatting.Compact;

// No arguments: serve the API and dashboard. Arguments: headless (run / graph / verify-audit).
Console.OutputEncoding = System.Text.Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args.Length == 0 ? args : []);
builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.WithProperty("service", "sdlc-orchestrator")
    .WriteTo.Console(new CompactJsonFormatter()));
builder.Services.AddSingleton(RepositoryPaths.Locate());
builder.Services.AddSingleton<RunRegistry>();
builder.Services.AddSingleton<RunService>();
builder.Services.AddHealthChecks();
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

var app = builder.Build();

if (args.Length > 0)
{
    using var ctrlC = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; ctrlC.Cancel(); };
    var service = app.Services.GetRequiredService<RunService>();
    var paths = app.Services.GetRequiredService<RepositoryPaths>();
    try
    {
        return await HeadlessMode.RunAsync(args, service, paths, ctrlC.Token);
    }
    catch (Exception e) when (e is InvalidOperationException or FileNotFoundException or ArgumentException)
    {
        Console.Error.WriteLine($"error: {e.Message}");
        return 1;
    }
}

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // The dashboard changes often during development; make browsers revalidate on every load.
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate",
});
app.MapHealthChecks("/health/live");
app.MapWorkflows();
app.MapRuns();
app.MapEventStream();

Console.WriteLine($"dashboard: {builder.Configuration["urls"]}");
await app.RunAsync();
return 0;
