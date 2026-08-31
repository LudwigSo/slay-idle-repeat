using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>The inbox store: the one way anything reaches a player's messages.</summary>
/// <remarks>
/// <para>
/// <c>23</c> §4.2's port, with one member added. The section writes four — <see cref="GetActiveAsync"/>,
/// <see cref="AppendAsync"/>, <see cref="MarkClaimedAsync"/> and <see cref="DequeueExpiringAsync"/> —
/// and the nightly job's own requirement is <em>"auto-grants attachments on expiring messages and
/// then deletes them"</em>, which the four cannot express. <see cref="DeleteAsync"/> is that
/// deletion, and it is the same member the capacity rule uses to prune.
/// </para>
/// <para>
/// 🔒 <b>What is NOT here is the whole of what keeps the store dumb.</b> Which messages a capacity
/// rule may prune, and which categories never expire, are RULES — they live in
/// <c>InboxCapacity</c> and <c>MessageCategories</c> where a unit test can drive them, not in SQL
/// where only a live database could. This port stores rows and answers questions about them; it
/// decides nothing.
/// </para>
/// <para>
/// 🔒 <b>Server → player only.</b> There is no member here that names a sender, and none that reads
/// one player's messages on behalf of another. A message arrives through
/// <see cref="AppendAsync"/>, which is called by ops tooling and by server-side systems, and by
/// nothing a player can reach.
/// </para>
/// <para>
/// 🔒 <b>Claiming IS atomic with the player's own commit, and this port's own member is not how.</b>
/// <see cref="MarkClaimedAsync"/> here owns its own transaction and serves callers that have none —
/// the expiry sweep's auto-grant. An accepted <c>CLAIM_INBOX</c> does not use it: the ids ride
/// <c>CommandCommit.Claim</c> and the unit of work stamps them inside the same transaction as the
/// snapshot whose wallet the claim paid into, through the caller-owned-transaction overload the
/// Postgres adapter exposes beside the ones the player repository and the economy event log carry.
/// A reward that was PAID and still read as claimable would be a double-grant next claim, which is
/// what one-command-one-transaction exists to prevent.
/// </para>
/// </remarks>
public interface IMessageRepository
{
    /// <summary>
    /// The player's live messages: everything that has not expired, claimed or not.
    /// </summary>
    /// <param name="id">Whose inbox to read.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The live messages, in any order. Empty for a player with none — never <c>null</c>.</returns>
    /// <remarks>
    /// A claimed message is still live: it stays in the list until it expires or is pruned, because
    /// what a player sees after collecting a reward is the message that gave it to them, not a gap.
    /// A message with no expiry is live for ever, which is what the two record categories are.
    /// </remarks>
    Task<IReadOnlyList<PlayerMessage>> GetActiveAsync(PlayerId id, CancellationToken ct);

    /// <summary>Stores one message.</summary>
    /// <param name="message">The message to store. Its id is the row's key.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// Idempotent on the message id: appending a message whose id is already stored leaves the
    /// stored row exactly as it was. A segment send that failed halfway is retried whole, and the
    /// half that landed must not be paid a second time by being written a second time.
    /// </remarks>
    Task AppendAsync(PlayerMessage message, CancellationToken ct);

    /// <summary>Stamps the named messages claimed, so their attachments are never granted again.</summary>
    /// <param name="id">Whose messages.</param>
    /// <param name="ids">The messages to stamp. An empty list stamps nothing.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// Idempotent, and it keeps the FIRST stamp: a message already claimed keeps the moment it was
    /// claimed at, because that timestamp is the record of when the reward was actually paid.
    /// An id this player does not own is ignored rather than refused — the caller has already been
    /// told so by <see cref="GetActiveAsync"/>, and a throw here would fail a claim that succeeded.
    /// </remarks>
    Task MarkClaimedAsync(PlayerId id, IReadOnlyList<MessageId> ids, CancellationToken ct);

    /// <summary>
    /// The next batch of messages whose expiry has fallen due — across every player, oldest expiry
    /// first — for the nightly auto-grant job.
    /// </summary>
    /// <param name="asOfUtc">The instant to judge expiry against.</param>
    /// <param name="limit">How many rows to answer with at most. Positive.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The due messages, oldest expiry first. Empty when nothing is due.</returns>
    /// <remarks>
    /// It reads and does not remove, despite the name <c>23</c> §4.2 gives it: the job grants what a
    /// message was holding and only then deletes it, so a store that removed the row on read would
    /// destroy every unclaimed reward the job crashed halfway through — the one thing the expiry
    /// rule exists to make impossible.
    /// <para>
    /// A message with no expiry is never due. That is what makes the two record categories permanent
    /// without the job needing to know which they are.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<PlayerMessage>> DequeueExpiringAsync(
        DateTimeOffset asOfUtc, int limit, CancellationToken ct);

    /// <summary>Removes the named messages.</summary>
    /// <param name="ids">The messages to remove. An empty list removes nothing.</param>
    /// <param name="ct">Cancellation.</param>
    /// <remarks>
    /// The two callers are the expiry job — after it has granted what the message was holding — and
    /// the capacity rule. Neither may hand it a message the rules call the record; deciding that is
    /// <c>MessageCategories.IsPermanentRecord</c>'s, and this member does as it is told.
    /// </remarks>
    Task DeleteAsync(IReadOnlyList<MessageId> ids, CancellationToken ct);
}
