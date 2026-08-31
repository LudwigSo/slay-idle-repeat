using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Events;

namespace SlayIdleRepeat.Application.Services.Analytics;

/// <summary>
/// The sink that feeds operational telemetry: one <see cref="DomainEventsMetric"/> measurement per
/// domain event, tagged with the event's type, so the dashboard can see what the domain is doing.
/// </summary>
public sealed class TelemetryEventSink : IDomainEventSink
{
    /// <summary>The counter's name.</summary>
    public const string DomainEventsMetric = "domain_events";

    /// <summary>The tag carrying the event's type name.</summary>
    public const string EventTypeTag = "type";

    private readonly ITelemetryPort _telemetry;

    /// <summary>Builds the sink over the port it records through.</summary>
    /// <param name="telemetry">Where the measurements go.</param>
    /// <exception cref="ArgumentNullException"><paramref name="telemetry"/> is null.</exception>
    public TelemetryEventSink(ITelemetryPort telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);

        _telemetry = telemetry;
    }

    /// <inheritdoc/>
    public Task ReceiveAsync(DispatchedEvents batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);

        foreach (var domainEvent in batch.Events)
        {
            _telemetry.RecordMetric(
                DomainEventsMetric, 1d, (EventTypeTag, domainEvent.GetType().Name));
        }

        return Task.CompletedTask;
    }
}
