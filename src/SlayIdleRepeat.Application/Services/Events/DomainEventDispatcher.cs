using SlayIdleRepeat.Core.Events;

namespace SlayIdleRepeat.Application.Services.Events;

/// <summary>One sink's failure to receive a command's events, carried out on the command's result.</summary>
/// <param name="Sink">The sink that failed, named so a reader can tell which side channel is down.</param>
/// <param name="Error">What it threw, as text — enough to diagnose without re-running the command.</param>
public sealed record EventDispatchFailure(string Sink, string Error);

/// <summary>Fans one accepted command's events out to every registered sink, in registration order.</summary>
/// <remarks>
/// A sink that throws is collected, never rethrown. Dispatch runs after the commit, so a failed sink
/// would otherwise turn a command that provably happened into an error the player is shown — the
/// state would be changed and the answer would say it was not.
/// </remarks>
public sealed class DomainEventDispatcher
{
    /// <summary>Builds a dispatcher over an ordered list of sinks.</summary>
    /// <param name="sinks">The sinks, in the order they are delivered to. Never null; may be empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sinks"/> is null, or holds a null sink.</exception>
    public DomainEventDispatcher(IReadOnlyList<IDomainEventSink> sinks) => throw new NotImplementedException();

    /// <summary>Delivers <paramref name="events"/> to every sink and collects whatever failed.</summary>
    /// <param name="events">One command's events, in order. Never null; may be empty.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>One entry per sink that threw, in registration order. Empty when every sink took the batch.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public Task<IReadOnlyList<EventDispatchFailure>> DispatchAsync(
        IReadOnlyList<DomainEvent> events, CancellationToken ct) => throw new NotImplementedException();
}
