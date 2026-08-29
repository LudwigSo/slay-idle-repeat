using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>The inbox messages one accepted command paid for, and the instant to record the payment at.</summary>
/// <param name="Player">Whose messages. A message is only ever claimable by its own owner.</param>
/// <param name="Messages">The ids the command's <c>MailClaimed</c> events named, in claim order. Never empty — a commit with nothing to stamp carries no claim at all.</param>
/// <param name="AtUtc">The command's own instant, so the stamp dates the payment rather than the write.</param>
/// <remarks>
/// One value rather than three nullable fields on the commit: a caller cannot name the messages
/// without also naming whose they are and when they were paid, so "all three or none" is the
/// compiler's to check — the same argument <see cref="CommandCommit"/> itself is built on.
/// </remarks>
public sealed record MailClaim(
    PlayerId Player,
    IReadOnlyList<MessageId> Messages,
    DateTimeOffset AtUtc)
{
    /// <summary>The ids to stamp, in claim order. Never null and never empty.</summary>
    public IReadOnlyList<MessageId> Messages { get; } =
        Messages is null || Messages.Count == 0
            ? throw new ArgumentException(
                "A claim with no messages is a commit carrying the shape of a payment nothing was " +
                "paid for. Pass null for the claim instead.",
                nameof(Messages))
            : Messages;
}

/// <summary>Everything one accepted — or one refused — command leaves behind, as a single value.</summary>
/// <param name="Scope">The sequencing domain the outcome is recorded and counted in.</param>
/// <param name="Outcome">The full response envelope and the sequence it consumed.</param>
/// <param name="OutcomeTtl">How long the record stays replayable. Positive.</param>
/// <param name="State">The aggregate snapshots to store, or <c>null</c> on a refusal — nothing moved.</param>
/// <param name="EconomyEvents">The economy-log rows the command produced, in event order. Empty on a refusal.</param>
/// <param name="OpensScope">The run scope an accepted opening command created, or <c>null</c> for every other command.</param>
/// <param name="Claim">The inbox messages an accepted claim paid for, or <c>null</c> for every command that paid none.</param>
/// <remarks>
/// <para>
/// The atom is a type rather than a convention: a caller cannot describe the snapshots without also
/// describing the outcome record, so "all of it or none of it" is checked by the compiler instead of
/// by everybody remembering to make the same three calls in the same order.
/// </para>
/// <para>
/// 🔒 <see cref="Claim"/> is here because a reward that is PAID and still reads as claimable is a
/// double-grant waiting for the next claim: the player's wallet moved inside the commit and the
/// message that authorised it would be stamped outside, so a crash in between leaves the two
/// disagreeing. That was M5-08's registered absence, owned by the commit rule; it closes by the
/// stamp riding the same transaction as the snapshots it paid for, rather than by anyone being
/// careful about the order of two calls.
/// </para>
/// <para>
/// A refusal is still a commit. It carries the record alone, because a refusal is a decided answer
/// and recording it is what lets its duplicate replay instead of being decided a second time.
/// </para>
/// </remarks>
public sealed record CommandCommit(
    IdempotencyScope Scope,
    RecordedCommandOutcome Outcome,
    TimeSpan OutcomeTtl,
    PlayerProfile? State,
    IReadOnlyList<EconomyEventRecord> EconomyEvents,
    IdempotencyScope? OpensScope,
    MailClaim? Claim = null)
{
    /// <summary>The full response envelope and the sequence it consumed. Never null.</summary>
    public RecordedCommandOutcome Outcome { get; } =
        Outcome ?? throw new ArgumentNullException(nameof(Outcome));

    /// <summary>The economy-log rows, in event order. Never null; empty is the ordinary case.</summary>
    public IReadOnlyList<EconomyEventRecord> EconomyEvents { get; } =
        EconomyEvents ?? throw new ArgumentNullException(nameof(EconomyEvents));
}

/// <summary>The transaction boundary one processed command is committed inside.</summary>
/// <remarks>
/// <para>
/// One accepted command is one commit carrying the aggregate snapshots, the idempotency outcome
/// record with the scope's sequence advance, the appended economy-log rows, and the claimed-message
/// stamp. An implementation either lands the whole <see cref="CommandCommit"/> or lands none of it;
/// there is no partial answer and no second call to make it whole.
/// </para>
/// <para>
/// The unit of work receives what it commits rather than accumulating it, so no half-built ambient
/// session exists that a caller could leak or forget to close.
/// </para>
/// <para>
/// What deliberately rides OUTSIDE it: the analytics and telemetry fan-out, and any cache
/// population. Both are loss-tolerant and neither may hold a player's command open.
/// </para>
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Commits one processed command, whole.</summary>
    /// <param name="commit">Everything the command leaves behind.</param>
    /// <param name="ct">Cancellation.</param>
    Task CommitAsync(CommandCommit commit, CancellationToken ct);
}
