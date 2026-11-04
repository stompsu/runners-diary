using Azure.Monitor.OpenTelemetry.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ServiceDiscovery;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

// Adds common Aspire services: service discovery, resilience, health checks, and OpenTelemetry.
// This project should be referenced by each service project in your solution.
// To learn more about using this project, see https://aka.ms/aspire/service-defaults
public static class Extensions
{
    /// <summary>The ActivitySource and Meter of the AI chat client pipeline (Infrastructure <c>ChatClientFactory.TelemetrySourceName</c>).</summary>
    private const string AiTelemetrySourceName = "RunnersDiary.Ai";

    /// <summary>Environment variable (configuration key) that Azure sets for App Insights; its presence turns the Azure Monitor exporter on.</summary>
    public const string ApplicationInsightsConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

    /// <summary>Trace sampling budget per second (ADR 0034).</summary>
    private const double AzureMonitorTracesPerSecond = 5;

    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Turn on resilience by default
            http.AddStandardResilienceHandler();

            // Turn on service discovery by default
            http.AddServiceDiscovery();
        });

        // Uncomment the following to restrict the allowed schemes for service discovery.
        // builder.Services.Configure<ServiceDiscoveryOptions>(options =>
        // {
        //     options.AllowedSchemes = ["https"];
        // });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics.AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    // M6 Training Insights: gen_ai.client.token.usage and operation duration from the chat client pipeline (ADR 0027).
                    .AddMeter(AiTelemetrySourceName);
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddSource(AiTelemetrySourceName)
                    .AddAspNetCoreInstrumentation(tracing =>
                        // Exclude health check requests from tracing
                        tracing.Filter = context =>
                            !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                            && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    // Uncomment the following line to enable gRPC instrumentation (requires the OpenTelemetry.Instrumentation.GrpcNetClient package)
                    //.AddGrpcClientInstrumentation()
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        // Application Insights (ADR 0034): only when Azure provides the connection string, so local runs and tests keep OTLP only.
        // The exporter reads APPLICATIONINSIGHTS_CONNECTION_STRING itself; no key is ever copied into configuration files.
        if (!string.IsNullOrWhiteSpace(builder.Configuration[ApplicationInsightsConnectionStringKey]))
        {
            builder.Services.AddOpenTelemetry()
                .UseAzureMonitor(options =>
                {
                    // Rate-limited sampling keeps a request flood from exhausting the Log Analytics daily cap.
                    options.TracesPerSecond = AzureMonitorTracesPerSecond;
                });
        }

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            // Add a default liveness check to ensure app is responsive
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Mapped in all environments. Container Apps probes (startup, liveness, readiness) must target /alive only (ADR 0032/0034):
        // it runs the "live" check, which never touches SQL or Blob, so probes cannot wake an auto-paused serverless database
        // (ADR 0006). /health runs every registered check (including the EF Core SQL check) and is for manual diagnosis and
        // the deploy smoke test only. Both are explicitly anonymous (M2's fallback auth policy must not break platform probes)
        // and return only the aggregate status text, never per-check details.

        // All health checks must pass for app to be considered ready to accept traffic after starting
        app.MapHealthChecks(HealthEndpointPath)
            .AllowAnonymous();

        // Only health checks tagged with the "live" tag must pass for app to be considered alive
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        })
            .AllowAnonymous();

        return app;
    }
}
