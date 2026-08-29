using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Inbox;

/// <summary>
/// The two halves of a claim that belong to this layer: loading the inbox the rules read, and
/// stamping the messages the rules said were paid.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately a small type of its own rather than lines inside the command use case. What the use
/// case gains is a field and two calls; everything about which commands need an inbox, what an empty
/// one looks like and which ids a batch claimed lives here, where a unit test drives it without a
/// command pipeline around it.
/// </para>
/// <para>
/// 🔒 <b>The stamp is not here, and its absence is the shape of the commit rule.</b> This type reads
/// the inbox and names which ids a batch claimed; the WRITE rides the accepted command's own
/// transaction, carried on <c>CommandCommit.Claim</c>, beside the player snapshot whose wallet the
/// same claim moved. A stamping method here would be the second way to record a payment, and two
/// ways are exactly how a reward comes to be paid while still reading as claimable.
/// </para>
/// </remarks>
public sealed class InboxCommandSupport
{
    private readonly IMessageRepository _messages;

    /// <summary>Builds the support over the inbox store.</summary>
    /// <param name="messages">The inbox store.</param>
    /// <exception cref="ArgumentNullException"><paramref name="messages"/> is null.</exception>
    public InboxCommandSupport(IMessageRepository messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        _messages = messages;
    }

    /// <summary>Whether this command reads the inbox and therefore needs it loaded.</summary>
    /// <param name="command">The command about to be applied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="command"/> is null.</exception>
    /// <remarks>
    /// A predicate rather than "load it always": the inbox is a second table, and reading it on every
    /// roll of the dice would put a query on the hottest path in the game to serve one command.
    /// </remarks>
    public static bool Reads(GameCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command is ClaimInboxCommand;
    }

    /// <summary>The player's inbox as the rules read it.</summary>
    /// <param name="player">Whose inbox.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The projection — <see cref="InboxView.Empty"/> for a player with no messages.</returns>
    public async Task<InboxView> LoadAsync(PlayerId player, CancellationToken ct)
    {
        var stored = await _messages.GetActiveAsync(player, ct).ConfigureAwait(false);

        return stored.Count == 0
            ? InboxView.Empty
            : new InboxView(stored.Select(m => m.ToProjection()).ToArray());
    }

    /// <summary>The messages an accepted command's events say were paid.</summary>
    /// <param name="events">The batch the domain produced.</param>
    /// <returns>The ids to stamp, in the order they were claimed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    public static IReadOnlyList<MessageId> ClaimedIn(IReadOnlyList<DomainEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return events.OfType<MailClaimed>().Select(e => e.MessageId).ToArray();
    }
}
