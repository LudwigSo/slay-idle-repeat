using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Events;

/// <summary>One accepted command's whole delivery: who acted, what they sent, where it landed, and what it produced.</summary>
/// <param name="Player">Whose command it was — what lets a sink attribute anything at all.</param>
/// <param name="Command">The command as accepted.</param>
/// <param name="State">The committed state the command produced.</param>
/// <param name="Events">The command's events, in the order the domain produced them. Never null; may be empty.</param>
/// <remarks>
/// A single record rather than a growing parameter list, so the sink signature never has to move
/// again when the next sink needs one more piece of the command's context.
/// </remarks>
public sealed record DispatchedEvents(
    PlayerId Player, GameCommand Command, WorldSlice State, IReadOnlyList<DomainEvent> Events)
{
    /// <inheritdoc cref="DispatchedEvents"/>
    public GameCommand Command { get; } = Command ?? throw new ArgumentNullException(nameof(Command));

    /// <inheritdoc cref="DispatchedEvents"/>
    public WorldSlice State { get; } = State ?? throw new ArgumentNullException(nameof(State));

    /// <inheritdoc cref="DispatchedEvents"/>
    public IReadOnlyList<DomainEvent> Events { get; } = Events ?? throw new ArgumentNullException(nameof(Events));
}

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
/// A sink receives one command's batch, its events in the order the domain produced them, after the
/// state it describes is already committed. Throwing is allowed and is collected rather than
/// propagated: the command already happened, so a failed side channel must not report it as refused.
/// </para>
/// </remarks>
public interface IDomainEventSink
{
    /// <summary>Delivers one accepted command's batch.</summary>
    /// <param name="batch">The command, its player, its committed state and its events. Never null.</param>
    /// <param name="ct">Cancellation.</param>
    Task ReceiveAsync(DispatchedEvents batch, CancellationToken ct);
}
