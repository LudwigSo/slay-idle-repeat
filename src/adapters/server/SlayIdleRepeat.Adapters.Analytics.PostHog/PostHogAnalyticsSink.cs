using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.Analytics.PostHog;

/// <summary>
/// <see cref="IAnalyticsSinkPort"/> over PostHog's batch capture endpoint, hand-rolled over
/// <see cref="HttpClient"/> — the port boundary is the isolation, so no vendor SDK is pinned.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Track"/> never blocks and never throws for a backend problem: events queue in a
/// bounded in-memory buffer and are POSTed as one <c>/batch</c> JSON body. A full buffer drops the
/// oldest; a failed POST drops the batch. Disabled means every event is dropped on arrival.
/// </para>
/// <para>
/// The handler is injected so the wire behaviour is testable without a network.
/// </para>
/// </remarks>
public sealed class PostHogAnalyticsSink : IAnalyticsSinkPort, IDisposable
{
    private readonly PostHogOptions _options;
    private readonly HttpMessageHandler _handler;

    /// <summary>Builds the sink over its configuration and the HTTP handler it posts through.</summary>
    /// <param name="options">The adapter's configuration.</param>
    /// <param name="handler">The transport. Owned by the caller.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public PostHogAnalyticsSink(PostHogOptions options, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);

        _options = options;
        _handler = handler;
    }

    /// <inheritdoc/>
    public void Track(PlayerId player, AnalyticsEvent analyticsEvent)
    {
        ArgumentNullException.ThrowIfNull(analyticsEvent);

        _ = _options;
        _ = _handler;

        throw new NotImplementedException();
    }

    /// <summary>
    /// Drains the queue now — the background flush's own step, callable so composition can flush at
    /// shutdown and a test can flush deterministically. Never throws for a backend problem.
    /// </summary>
    /// <param name="ct">Cancellation.</param>
    public Task FlushAsync(CancellationToken ct) => throw new NotImplementedException();

    /// <inheritdoc/>
    public void Dispose()
    {
        // Nothing owned yet; the queue and its flush loop arrive with the implementation.
    }
}
