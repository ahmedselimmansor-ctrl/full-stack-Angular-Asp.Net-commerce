using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Sama.Shared.Infrastructure.Observability;

/// <summary>Named instruments owned by the platform, as opposed to framework-emitted ones.</summary>
public static class SamaTelemetry
{
    public const string ActivitySourceName = "Sama";
    public const string MeterName = "Sama";

    public static ActivitySource ActivitySource { get; } = new(ActivitySourceName);

    public static Meter Meter { get; } = new(MeterName);
}

public static class TelemetryRegistration
{
    /// <summary>
    /// Traces and metrics over OTLP. In GKE this points at the OpenTelemetry Collector
    /// DaemonSet, which forwards to Cloud Trace and Cloud Monitoring — the app stays
    /// vendor-neutral and the collector owns the GCP-specific credentials.
    /// </summary>
    public static IServiceCollection AddSamaTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var version = typeof(TelemetryRegistration).Assembly.GetName().Version?.ToString() ?? "0.0.0";
        var environmentName = configuration["ASPNETCORE_ENVIRONMENT"] ?? "unknown";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName, serviceVersion: version, serviceInstanceId: Environment.MachineName)
                .AddAttributes(
                [
                    new KeyValuePair<string, object>("deployment.environment", environmentName),
                    // Lets a trace be pinned to the exact pod without querying the cluster.
                    new KeyValuePair<string, object>("k8s.pod.name", Environment.MachineName),
                ]))
            .WithTracing(tracing => tracing
                .AddSource(SamaTelemetry.ActivitySourceName)
                .AddNpgsql()
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Probes would otherwise dominate the trace volume and the bill.
                    options.Filter = context => !IsProbe(context.Request.Path);
                    options.RecordException = true;
                })
                .AddHttpClientInstrumentation()
                .SetSampler(BuildSampler(configuration))
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(SamaTelemetry.MeterName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddNpgsqlInstrumentation()
                .AddOtlpExporter());

        return services;
    }

    /// <summary>
    /// Head sampling. Tracing every request at storefront volume is neither affordable nor
    /// useful; a parent-respecting ratio keeps a whole distributed trace either wholly
    /// sampled or wholly absent, which is what makes the sampled ones readable.
    /// </summary>
    private static ParentBasedSampler BuildSampler(IConfiguration configuration)
    {
        var ratio = configuration.GetValue<double?>("Telemetry:SamplingRatio") ?? 0.1;
        return new ParentBasedSampler(new TraceIdRatioBasedSampler(Math.Clamp(ratio, 0d, 1d)));
    }

    private static bool IsProbe(PathString path)
        => path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase)
           || path.StartsWithSegments("/metrics", StringComparison.OrdinalIgnoreCase);
}
