using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Model.Snapshots;

/// <summary>The persisted shape of the <c>Run</c> aggregate — flat, serialisable fields.</summary>
/// <param name="SchemaVersion"><see cref="SnapshotSchema.SchemaVersion"/> as it was when this row was written. Always the first field.</param>
/// <param name="Id">The aggregate root's identity.</param>
/// <param name="PlayerId">The player this run belongs to; a run is a child of its player, never a peer.</param>
/// <param name="RunSeed">The committed <c>runSeed</c>, derived once at <c>START_RUN</c>. The board, drops and draft of a resumed run are all re-derived from it.</param>
/// <param name="ChapterId">The chapter being played. No upper bound or existence check is enforced here.</param>
/// <param name="Tier">The difficulty tier, part of <see cref="RunSeed"/>'s derivation.</param>
/// <param name="LastAppliedAtUtc">The instant the last command was applied to this run — what the sliding run TTL is computed from. Distinct from <c>PlayerSnapshot.LastAppliedAtUtc</c>, which also advances on meta commands.</param>
/// <param name="Position">The linear node index the run stands on. Floor is <b>-1</b>, the virtual trailhead a started-but-unrolled run legitimately persists at.</param>
/// <param name="CurrentHp">The hero's current hit points. Never negative, never above <paramref name="MaxHp"/>.</param>
/// <param name="MaxHp">The hero's maximum hit points for this run. Stored rather than derived, so a resumed run can render its HP bar without recomputing the build. Never below 1.</param>
/// <param name="Gold">The run's <c>GOLD</c> balance, the one run-scoped currency. Never negative.</param>
/// <param name="RngStreamPositions">Per-stream RNG counters: stream name → next draw index. Sparse. The <c>combat</c> row counts battles started, not combat draws — combat's own draws restart at 0 per battle and are never persisted.</param>
/// <param name="AdUses">Per-run ad counts: placement id → uses so far. No period anchor — the run is the period.</param>
/// <param name="ResolvedMinigames">Per-tile minigame legality gate: linear node index → the minigame id resolved there. Sparse.</param>
/// <param name="PendingForkJunctionPosition">The paused junction's identity, or <c>null</c> when not mid-move at a junction. Stored as a pair with <paramref name="PendingForkRemainingSteps"/>: both null, or both present.</param>
/// <param name="PendingForkRemainingSteps">Steps of the interrupted movement remaining once the chosen edge is taken. <c>null</c> exactly when <paramref name="PendingForkJunctionPosition"/> is.</param>
/// <param name="PendingTileKind">The tile kind the run has arrived at and not yet resolved, or <b>-1</b> for "no tile is pending".</param>
/// <param name="PendingTileLinearIndex">The linear node index of the pending tile. Meaningless (stored as 0) when <paramref name="PendingTileKind"/> is -1.</param>
/// <param name="PendingTileStage">The stage the pending tile belongs to — 1, 2, 3, or the boss stage value, which is also this field's value when no tile is pending.</param>
/// <param name="PendingEventCardId">The event card a pending <c>TILE_EVENT</c> has already drawn, or <c>""</c> when none has — never <c>null</c>. Exists so the card cannot be re-drawn across the two commands an event resolves over.</param>
/// <param name="Phase">The genuine server-side subset of the run's state machine. See <see cref="Primitives.RunPhase"/>.</param>
/// <param name="DraftPending">True once a won battle has closed and no draft command has resolved it yet.</param>
/// <param name="RerollChargesSpentThisStage">Reroll charges spent since the run's current stage began. Reset at every Stage Gate.</param>
/// <param name="StageGateDiceAnchor">The <c>dice</c> stream draw index the run's current stage began at.</param>
/// <param name="DraftBattleKind">The tile kind of the battle that set <paramref name="DraftPending"/>, captured before it clears. <b>-1</b> when no draft is pending.</param>
/// <param name="DraftBattleStage">The stage the battle named by <paramref name="DraftBattleKind"/> belonged to. <c>0</c> when no draft is pending.</param>
/// <param name="OwnedPerkTiers">The perks this run has drafted: perk id → owned internal tier (1-3). Sparse.</param>
/// <param name="BankedLegendXp">Legend XP banked so far this run, pending the run-end payout. Never negative.</param>
/// <param name="BankedSoulShards">Soul Shards banked so far this run, pending the same payout. Never negative.</param>
/// <param name="BossDefeated">Whether this run's Boss has been killed.</param>
/// <param name="DraftsSinceLegendaryOffered">Drafts drawn since one last offered a Legendary option. Never negative.</param>
/// <param name="DraftsWithoutAboveCommon">Consecutive drafts that offered nothing above Common. Never negative.</param>
/// <param name="DraftsWithoutOwnedUpgrade">Consecutive drafts that offered no owned-perk upgrade. Never negative.</param>
/// <remarks>
/// Flat: the only structured members are <see cref="Primitives.RunId"/> and
/// <see cref="Primitives.PlayerId"/>, plus the two dictionaries — a positional record with no members
/// outside the primary constructor, which is what makes the field-order pin able to describe it at
/// all. Every timestamp is refused unless its offset is zero, checked by <c>Run.Rehydrate</c>. The
/// drafted perks, held consumables/Escape Rope flag and curses are still deferred, each guarded by a
/// <c>GapRegister</c> entry; the board is never stored — it regenerates deterministically from
/// <see cref="RunSeed"/> and <see cref="RngStreamPositions"/>'s <c>board</c> entry on every command.
/// Adding, removing or reordering any field here bumps <see cref="SnapshotSchema.SchemaVersion"/> and
/// is a versioned migration, never silent; the field list is pinned in
/// <c>tests/SlayIdleRepeat.Core.Tests/Model/Snapshots/SnapshotFieldOrder.json</c>.
/// </remarks>
public sealed record RunSnapshot(
    int SchemaVersion,
    RunId Id,
    PlayerId PlayerId,
    ulong RunSeed,
    int ChapterId,
    DifficultyTier Tier,
    DateTimeOffset LastAppliedAtUtc,
    int Position,
    int CurrentHp,
    int MaxHp,
    long Gold,
    IReadOnlyDictionary<string, ulong> RngStreamPositions,
    IReadOnlyDictionary<string, long> AdUses,
    IReadOnlyDictionary<int, string> ResolvedMinigames,
    int? PendingForkJunctionPosition,
    int? PendingForkRemainingSteps,
    int PendingTileKind,
    int PendingTileLinearIndex,
    int PendingTileStage,
    string PendingEventCardId,
    RunPhase Phase = RunPhase.InProgress,
    bool DraftPending = false,
    int RerollChargesSpentThisStage = 0,
    ulong StageGateDiceAnchor = 0,
    int DraftBattleKind = -1,
    int DraftBattleStage = 0,
    IReadOnlyDictionary<string, int>? OwnedPerkTiers = null,
    long BankedLegendXp = 0,
    long BankedSoulShards = 0,
    bool BossDefeated = false,
    int DraftsSinceLegendaryOffered = 0,
    int DraftsWithoutAboveCommon = 0,
    int DraftsWithoutOwnedUpgrade = 0);
