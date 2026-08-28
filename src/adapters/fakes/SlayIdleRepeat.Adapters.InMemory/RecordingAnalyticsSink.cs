using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The in-memory fake for <see cref="IAnalyticsSinkPort"/>: keeps every tracked
/// (player, event) pair, in call order, so a scenario can assert exactly what analytics was told.
/// </summary>
public sealed class RecordingAnalyticsSink : IAnalyticsSinkPort
{
    private readonly object _gate = new();
    private readonly List<(PlayerId Player, AnalyticsEvent Event)> _tracked = [];

    /// <summary>Every tracked call, in the order it arrived.</summary>
    public IReadOnlyList<(PlayerId Player, AnalyticsEvent Event)> Tracked
    {
        get
        {
            lock (_gate)
            {
                return _tracked.ToArray();
            }
        }
    }

    /// <inheritdoc/>
    public void Track(PlayerId player, AnalyticsEvent analyticsEvent)
    {
        ArgumentNullException.ThrowIfNull(analyticsEvent);

        lock (_gate)
        {
            _tracked.Add((player, analyticsEvent));
        }
    }
}
