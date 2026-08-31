using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SlayIdleRepeat.Adapters.Telemetry.OpenTelemetry;

/// <summary>
/// The adapter's composition hook: subscribes the OTel SDK to <see cref="OpenTelemetryTelemetry"/>'s
/// source and meter and attaches the OTLP exporters. Endpoint, protocol, service name and sampling
/// all come from the specification's own <c>OTEL_*</c> environment variables, which the SDK reads
/// with no code here.
/// </summary>
public static class OpenTelemetrySdkRegistration
{
    /// <summary>Registers tracing and metrics export for everything the adapter records.</summary>
    /// <param name="services">The host's service collection.</param>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddSlayIdleRepeatOpenTelemetry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource(OpenTelemetryTelemetry.ActivitySourceName)
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(OpenTelemetryTelemetry.MeterName)
                .AddOtlpExporter());

        return services;
    }
}
