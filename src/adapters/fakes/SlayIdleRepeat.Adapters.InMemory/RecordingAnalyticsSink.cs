using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The in-memory fake for <see cref="IAnalyticsSinkPort"/>: keeps every tracked
/// (player, event) pair, in call order, so a scenario can assert exactly what analytics was told.
/// </summary>
public sealed class RecordingAnalyticsSink : IAnalyticsSinkPort
{
    /// <summary>Every tracked call, in the order it arrived.</summary>
    public IReadOnlyList<(PlayerId Player, AnalyticsEvent Event)> Tracked =>
        throw new NotImplementedException();

    /// <inheritdoc/>
    public void Track(PlayerId player, AnalyticsEvent analyticsEvent) =>
        throw new NotImplementedException();
}
