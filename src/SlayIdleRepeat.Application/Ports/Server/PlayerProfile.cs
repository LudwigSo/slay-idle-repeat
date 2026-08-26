using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>One player's authoritative persisted row: the player, and whatever run the player is in.</summary>
/// <param name="Player">The player's snapshot. Never null — a profile always names a player.</param>
/// <param name="ActiveRun">The run the player is in, or <c>null</c> when they are outside one.</param>
/// <remarks>
/// <para>
/// The pair travels together for the reason <c>StoredSlice</c> pairs them: one accepted command is
/// one commit of both, and a store that could hold a player disagreeing with its own run has no
/// ordering of two writes that repairs it. The stored document is the exact <c>SnapshotCodec</c>
/// bytes — the bytes <c>Rehydrate</c> reads are the system of record.
/// </para>
/// <para>
/// Deliberately NOT the sequencing counters: those are transport state living beside the snapshots,
/// owned by <see cref="IIdempotencyStore"/>, and putting them here would put them inside the
/// snapshot commit a caller composes.
/// </para>
/// </remarks>
public sealed record PlayerProfile(PlayerSnapshot Player, RunSnapshot? ActiveRun)
{
    /// <summary>The player's snapshot. Never null.</summary>
    public PlayerSnapshot Player { get; } = Player ?? throw new ArgumentNullException(nameof(Player));
}
