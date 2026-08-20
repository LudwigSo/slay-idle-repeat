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
/// <param name="Position">The <b>identity</b> of the board node the run stands on, not a track offset: equal to the linear index for every spine node and for the boss, different inside a fork branch, where a branch node shares its linear index with the spine node the same distance ahead. Floor is <b>-1</b>, the virtual trailhead a started-but-unrolled run legitimately persists at.</param>
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
/// <param name="DraftPending">True while a perk draft is waiting and no draft command has resolved it yet — set by a won battle, and by the run opening.</param>
/// <param name="DraftBattleKind">The tile kind the pending draft draws its rarity band against — the battle's own kind for a post-battle draft, and <c>Empty</c> for the run's opening draft, which no battle caused. <b>-1</b> when no draft is pending.</param>
/// <param name="DraftBattleStage">The stage the battle named by <paramref name="DraftBattleKind"/> belonged to. <c>0</c> when no draft is pending.</param>
/// <param name="OwnedPerkTiers">The perks this run has drafted: perk id → owned internal tier (1-3). Sparse.</param>
/// <param name="BankedLegendXp">Legend XP banked so far this run, pending the run-end payout. Never negative.</param>
/// <param name="BankedSoulShards">Soul Shards banked so far this run, pending the same payout. Never negative.</param>
/// <param name="BossDefeated">Whether this run's Boss has been killed.</param>
/// <param name="DraftsSinceLegendaryOffered">Drafts picked from since one last offered a Legendary option. Never negative.</param>
/// <param name="DraftsWithoutAboveCommon">Consecutive drafts picked from that offered nothing above Common. Never negative.</param>
/// <param name="DraftsWithoutOwnedUpgrade">Consecutive drafts picked from that offered no owned-perk upgrade. Never negative.</param>
/// <param name="StartingLoadout">
/// What the hero was wearing when the run started, frozen for the run's whole life — `07` §4's
/// "snapshotted at run start". ⚠️ <c>null</c> is a <b>fault</b>: a run whose starting loadout went
/// missing is not a run fought naked, and reading it as empty would silently strip the build for the
/// rest of the run. It holds instance IDENTITIES, so an item enhanced between two commands is worn
/// at its new value; what is frozen is which items are equipped.
/// </param>
/// <param name="ItemsAtOrAboveFloorBand">
/// How many items at or above the session floor's authored band this run has produced. Never
/// negative. Carried on the run rather than derived from the stock at run end: the stock is the
/// player's and holds items from every run they have ever made, so a tally taken from it could not
/// tell what THIS session earned.
/// </param>
/// <param name="ShrineBuffs">
/// The shrine buffs taken this run, in the order they were taken (`03` §7a.5). A LIST, not a set:
/// the document says the same buff may appear again at a later shrine and <em>stacks additively</em>,
/// so a duplicate id is a second stack rather than a no-op, and the order is what makes the
/// canonical bytes reproducible.
/// </param>
/// <param name="RunBuffs">
/// The run buffs bought from a shop's slot 3 (`03` §7), same list semantics and same reason as
/// <paramref name="ShrineBuffs"/> — the magnitudes are flat and additive.
/// </param>
/// <param name="Curses">
/// The curses active on this run (`19` Part E). A list for canonical ordering, but with SET
/// semantics: `19` Part E gives curses no stacking, so <c>Run.ApplyCurse</c> refuses a duplicate
/// rather than adding a second copy.
/// </param>
/// <param name="Consumables">
/// Held consumables (`03` §7.1): consumable id → how many are held. Sparse, and a zero count is
/// removed rather than stored. Only the two HELD consumables ever appear — the two token
/// consumables convert to their charge at the till and are never held.
/// </param>
/// <param name="FixedDice">
/// The fixed dice this run holds: pips → how many of that number are held. Sparse, uncapped, and a
/// zero count is removed rather than stored. A fixed die is spent instead of a roll and moves the
/// hero exactly its number — see <c>Handlers.UseFixedDie</c>.
/// </param>
/// <param name="PendingFixedDieChoices">
/// Fixed dice granted but not yet given a number by the player. Never negative. Persisted rather
/// than resolved at the grant because most grant sites carry no command a number could ride on: an
/// event outcome is drawn by weight, a minigame reward is decided by play, an ad and a set bonus are
/// passive. <c>CHOOSE_FIXED_DIE</c> answers one, and an owed choice blocks nothing.
/// </param>
/// <param name="EscapeRopeArmed">
/// Whether an Escape Rope is armed (`03` §7.1). Only one may be armed at a time, which is why this
/// is a flag and not a count, and it persists across rolls until it fires.
/// </param>
/// <param name="FreeDraftRerolls">
/// Free perk-draft rerolls held, from Draft Tokens (`03` §7.1). Never negative. Not per stage: the
/// token is bought and held until spent.
/// </param>
/// <param name="ShopOfferDraw">
/// The <c>shop</c> stream position the shop offer currently on screen was drawn at, or <c>null</c>
/// when no shop is open. The offer itself is NOT persisted — it is re-derived from the run seed and
/// this position, the same way the board and a shrine's two options are, so a resumed run sees the
/// shop it left.
/// </param>
/// <param name="ShopSlotsPurchased">
/// A bitmask of which slots of the current offer have already been bought — bit <c>i</c> for slot
/// <c>i</c>. Never negative. Cleared with the offer on a refresh and on leaving the shop.
/// </param>
/// <param name="ShopRefreshesUsedThisVisit">
/// Refreshes spent at the shop currently open. Never negative. Per VISIT, not per run, which is what
/// `03` §7's "1 free refresh per shop visit" is counted against.
/// </param>
/// <remarks>
/// Flat: the only structured members are <see cref="Primitives.RunId"/> and
/// <see cref="Primitives.PlayerId"/>, plus the dictionaries — a positional record with no members
/// outside the primary constructor, which is what makes the field-order pin able to describe it at
/// all. Every timestamp is refused unless its offset is zero, checked by <c>Run.Rehydrate</c>. The
/// board is never stored — it regenerates deterministically from
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
    int DraftBattleKind = -1,
    int DraftBattleStage = 0,
    IReadOnlyDictionary<string, int>? OwnedPerkTiers = null,
    long BankedLegendXp = 0,
    long BankedSoulShards = 0,
    bool BossDefeated = false,
    int DraftsSinceLegendaryOffered = 0,
    int DraftsWithoutAboveCommon = 0,
    int DraftsWithoutOwnedUpgrade = 0,
    LoadoutSnapshot? StartingLoadout = null,
    int ItemsAtOrAboveFloorBand = 0,
    IReadOnlyList<string>? ShrineBuffs = null,
    IReadOnlyList<string>? RunBuffs = null,
    IReadOnlyList<string>? Curses = null,
    IReadOnlyDictionary<string, int>? Consumables = null,
    IReadOnlyDictionary<int, int>? FixedDice = null,
    int PendingFixedDieChoices = 0,
    bool EscapeRopeArmed = false,
    int FreeDraftRerolls = 0,
    ulong? ShopOfferDraw = null,
    int ShopSlotsPurchased = 0,
    int ShopRefreshesUsedThisVisit = 0);
