using SlayIdleRepeat.Application.Services.Events;

namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>Everything one accepted — or one refused — command leaves behind, as a single value.</summary>
/// <param name="Scope">The sequencing domain the outcome is recorded and counted in.</param>
/// <param name="Outcome">The full response envelope and the sequence it consumed.</param>
/// <param name="OutcomeTtl">How long the record stays replayable. Positive.</param>
/// <param name="State">The aggregate snapshots to store, or <c>null</c> on a refusal — nothing moved.</param>
/// <param name="EconomyEvents">The economy-log rows the command produced, in event order. Empty on a refusal.</param>
/// <param name="OpensScope">The run scope an accepted opening command created, or <c>null</c> for every other command.</param>
/// <remarks>
/// <para>
/// The atom is a type rather than a convention: a caller cannot describe the snapshots without also
/// describing the outcome record, so "all of it or none of it" is checked by the compiler instead of
/// by everybody remembering to make the same three calls in the same order.
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
    IdempotencyScope? OpensScope)
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
/// One accepted command is one commit carrying all three of the aggregate snapshots, the
/// idempotency outcome record with the scope's sequence advance, and the appended economy-log rows.
/// An implementation either lands the whole <see cref="CommandCommit"/> or lands none of it; there
/// is no partial answer and no second call to make it whole.
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
