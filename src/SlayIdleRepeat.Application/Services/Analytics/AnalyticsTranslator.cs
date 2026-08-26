using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Events;

namespace SlayIdleRepeat.Application.Services.Analytics;

/// <summary>
/// Turns one accepted command's batch into the analytics events it honestly carries — names from
/// <see cref="AnalyticsVocabulary"/> only, properties only from what the command and state say.
/// </summary>
/// <remarks>
/// A domain event with no authored analytics name is ignored, never improvised into one. No
/// timestamps: capture time is the backend's, and nothing in this layer reads a clock.
/// </remarks>
public static class AnalyticsTranslator
{
    /// <summary>The analytics events <paramref name="batch"/> carries, in emission order.</summary>
    /// <param name="batch">One accepted command's delivery.</param>
    /// <returns>Zero or more events. Empty when the batch carries nothing the vocabulary names.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="batch"/> is null.</exception>
    public static IReadOnlyList<AnalyticsEvent> Translate(DispatchedEvents batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        throw new NotImplementedException();
    }
}
