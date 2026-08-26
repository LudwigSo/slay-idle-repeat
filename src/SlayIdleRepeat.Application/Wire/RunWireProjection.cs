using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Wire;

/// <summary>
/// The client-visible projection of <c>RunSnapshot</c> — what rides the wire beside the profile and
/// what the wire <c>stateHash</c> covers on the run's side (14 §16.6).
/// </summary>
/// <remarks>
/// <para>
/// Field-for-field the persisted snapshot, in the persisted order, minus <c>RunSeed</c>: the seed
/// never leaves the server (`02` §2 — it is the whole run's future, and a client holding it could
/// read every board, drop and draft ahead of time). The M5 kickoff ruled the conflict with the
/// older §16.6 Input row in `02` §2's favour; the snapshot itself still persists the seed unchanged.
/// The draw counters (<c>RngStreamPositions</c>) stay — they are positions, worthless without the
/// seed, and the client mirror needs them.
/// </para>
/// <para>
/// Shape discipline, pin location and change consequences are
/// <see cref="PlayerWireProjection"/>'s, stated there once. Construction is through
/// <see cref="WireProjections.Of(RunSnapshot)"/> only.
/// </para>
/// </remarks>
public sealed record RunWireProjection(
    int SchemaVersion,
    RunId Id,
    PlayerId PlayerId,
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
    RunPhase Phase,
    bool DraftPending,
    int DraftBattleKind,
    int DraftBattleStage,
    IReadOnlyDictionary<string, int>? OwnedPerkTiers,
    long BankedLegendXp,
    long BankedSoulShards,
    bool BossDefeated,
    int DraftsSinceLegendaryOffered,
    int DraftsWithoutAboveCommon,
    int DraftsWithoutOwnedUpgrade,
    LoadoutSnapshot? StartingLoadout,
    int ItemsAtOrAboveFloorBand,
    IReadOnlyList<string>? ShrineBuffs,
    IReadOnlyList<string>? RunBuffs,
    IReadOnlyList<string>? Curses,
    IReadOnlyDictionary<string, int>? Consumables,
    IReadOnlyDictionary<int, int>? FixedDice,
    int PendingFixedDieChoices,
    bool EscapeRopeArmed,
    int FreeDraftRerolls,
    ulong? ShopOfferDraw,
    int ShopSlotsPurchased,
    int ShopRefreshesUsedThisVisit);
