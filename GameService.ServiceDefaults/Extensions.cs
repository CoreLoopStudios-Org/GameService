using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Microsoft.Extensions.Hosting;

public static class Extensions
{
    private const string HealthEndpointPath = "/health";
    private const string AlivenessEndpointPath = "/alive";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureOpenTelemetry();

        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // AddStandardResilienceHandler includes: Retry, Circuit Breaker, Timeout, Rate Limiter
            http.AddStandardResilienceHandler();

            http.AddServiceDiscovery();
        });

        return builder;
    }

    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
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
                    .AddRuntimeInstrumentation();
            })
            .WithTracing(tracing =>
            {
                tracing.AddSource(builder.Environment.ApplicationName)
                    .AddAspNetCoreInstrumentation(tracing => tracing.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                        && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath)
                    )
                    .AddHttpClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter) builder.Services.AddOpenTelemetry().UseOtlpExporter();

        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var healthChecks = builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        // Add Redis health check if connection string is configured
        var redisConnectionString = builder.Configuration.GetConnectionString("cache");
        if (!string.IsNullOrEmpty(redisConnectionString))
        {
            healthChecks.AddRedis(redisConnectionString, name: "redis", tags: ["ready"]);
        }

        // Add PostgreSQL health check if connection string is configured
        var postgresConnectionString = builder.Configuration.GetConnectionString("postgresdb");
        if (!string.IsNullOrEmpty(postgresConnectionString))
        {
            healthChecks.AddNpgSql(postgresConnectionString, name: "postgresql", tags: ["ready"]);
        }

        return builder;
    }

    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        // Map health checks in all environments for production readiness
        app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions
        {
            Predicate = _ => true // Include all health checks
        });

        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("live")
        });

        // Readiness endpoint - only checks external dependencies (Redis, PostgreSQL)
        app.MapHealthChecks("/ready", new HealthCheckOptions
        {
            Predicate = r => r.Tags.Contains("ready")
        });

        return app;
    }
}