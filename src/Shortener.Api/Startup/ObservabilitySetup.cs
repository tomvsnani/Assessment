using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Compact;

namespace Shortener.Api.Startup;

/// <summary>
/// Logs: Serilog, one JSON object per line (Splunk/ELK friendly), correlation id on every event.
/// Traces/metrics: OpenTelemetry; exported over OTLP only when OTEL_EXPORTER_OTLP_ENDPOINT is set.
/// </summary>
public static class ObservabilitySetup
{
    public const string ServiceName = "shortener-api";

    public static WebApplicationBuilder AddObservability(this WebApplicationBuilder builder)
    {
        builder.Host.UseSerilog((context, services, config) => config
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("service", ServiceName)
            .WriteTo.Console(new CompactJsonFormatter()));

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(ServiceName))
            .WithTracing(t => t.AddAspNetCoreInstrumentation())
            .WithMetrics(m => m.AddAspNetCoreInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            otel.UseOtlpExporter();
        }

        return builder;
    }
}
