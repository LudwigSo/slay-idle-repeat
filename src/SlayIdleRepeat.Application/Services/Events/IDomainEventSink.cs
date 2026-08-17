using SlayIdleRepeat.Core.Events;

namespace SlayIdleRepeat.Application.Services.Events;

/// <summary>Somewhere one accepted command's events are delivered after the command has committed.</summary>
/// <remarks>
/// <para>
/// 🔒 <b>Deliberately not a port.</b> The transports that will sit behind implementations of this —
/// analytics, telemetry, the durable economy log — are each their own port with their own owner and
/// their own real adapter. This is the in-process fan-out that will sit above those, so enrolling it
/// in the port catalogue would demand a real adapter for a seam whose only real adapters are the
/// ports it fans out to.
/// </para>
/// <para>
/// A sink receives one command's list, in the order the domain produced it, after the state it
/// describes is already committed. Throwing is allowed and is collected rather than propagated: the
/// command already happened, so a failed side channel must not report it as refused.
/// </para>
/// </remarks>
public interface IDomainEventSink
{
    /// <summary>Delivers one accepted command's events.</summary>
    /// <param name="events">The events, in the order the domain produced them. Never null; may be empty.</param>
    /// <param name="ct">Cancellation.</param>
    Task ReceiveAsync(IReadOnlyList<DomainEvent> events, CancellationToken ct);
}
