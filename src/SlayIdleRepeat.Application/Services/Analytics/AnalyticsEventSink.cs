using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Events;

namespace SlayIdleRepeat.Application.Services.Analytics;

/// <summary>
/// The sink that feeds analytics: translates each accepted command's batch and tracks every
/// resulting event against the batch's player.
/// </summary>
public sealed class AnalyticsEventSink : IDomainEventSink
{
    private readonly IAnalyticsSinkPort _analytics;

    /// <summary>Builds the sink over the port it tracks through.</summary>
    /// <param name="analytics">Where the translated events go.</param>
    /// <exception cref="ArgumentNullException"><paramref name="analytics"/> is null.</exception>
    public AnalyticsEventSink(IAnalyticsSinkPort analytics)
    {
        ArgumentNullException.ThrowIfNull(analytics);

        _analytics = analytics;
    }

    /// <inheritdoc/>
    public Task ReceiveAsync(DispatchedEvents batch, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(batch);

        _ = _analytics;

        throw new NotImplementedException();
    }
}
