using System.Collections.ObjectModel;
using System.Linq;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Tests.Model;

// Namespace SlayIdleRepeat.Core.Tests.Model, not ...Tests.Model.Run, and the files still sit
// under Model/Run/. Same reason the production aggregate does it: a child namespace named `Run`
// shadows the type `Run` for everything inside SlayIdleRepeat.Core.Tests.Model, so a test written
// here could not name the very class it is testing (CS0118).

/// <summary>
/// Hermetic <see cref="RunSnapshot"/> fixtures — one valid row, and a <c>With(...)</c> that
/// replaces exactly one field. The baseline numbers are test values and carry no design claim.
/// </summary>
internal static class RunSnapshots
{
    /// <summary>2026-08-12 09:41:07 UTC — an ordinary instant, on no boundary at all.</summary>
    internal static readonly DateTimeOffset Midmorning = new(2026, 8, 12, 9, 41, 7, TimeSpan.Zero);

    /// <summary>The run identity every fixture uses unless a test is about identity.</summary>
    internal static readonly RunId Id = new("RUN_TEST");

    /// <summary>The owning player every fixture uses unless a test is about parentage.</summary>
    internal static readonly PlayerId Owner = new("PLAYER_TEST");

    /// <summary>An arbitrary but fixed committed run seed, so a test that hashes is reproducible.</summary>
    internal const ulong Seed = 0x0123456789ABCDEFUL;

    /// <summary>An ordinal <c>stream → next draw index</c> map of the shape the snapshot carries.</summary>
    internal static IReadOnlyDictionary<string, ulong> Streams(
        params (string Stream, ulong Position)[] entries) =>
        new ReadOnlyDictionary<string, ulong>(
            entries.ToDictionary(e => e.Stream, e => e.Position, StringComparer.Ordinal));

    /// <summary>An ordinal <c>placement → uses</c> map of the shape the snapshot carries.</summary>
    internal static IReadOnlyDictionary<string, long> AdUses(
        params (string Placement, long Uses)[] entries) =>
        new ReadOnlyDictionary<string, long>(
            entries.ToDictionary(e => e.Placement, e => e.Uses, StringComparer.Ordinal));

    /// <summary>A <c>position → MG_* id</c> map of the shape the resolved-minigames field carries.</summary>
    internal static IReadOnlyDictionary<int, string> ResolvedMinigames(
        params (int Position, string MinigameId)[] entries) =>
        new ReadOnlyDictionary<int, string>(entries.ToDictionary(e => e.Position, e => e.MinigameId));

    /// <summary>A <c>perk id → owned tier</c> map of the shape the owned-perk-tiers field carries.</summary>
    internal static IReadOnlyDictionary<string, int> OwnedPerkTiers(
        params (string PerkId, int Tier)[] entries) =>
        new ReadOnlyDictionary<string, int>(
            entries.ToDictionary(e => e.PerkId, e => e.Tier, StringComparer.Ordinal));

    /// <summary><c>RunSnapshot.DraftBattleKind</c>'s "no draft pending" sentinel.</summary>
    internal const int NoDraftBattleKind = -1;

    /// <summary>
    /// A valid row: chapter 1 on <see cref="DifficultyTier.NORMAL"/>, at position 0, unhurt, with no
    /// Gold, no stream ever drawn from and no ad watched.
    /// </summary>
    internal static RunSnapshot Valid { get; } = With();

    /// <summary>
    /// The valid row with one reference-typed field replaced by <c>null</c> — <see cref="With"/>'s
    /// optional parameters read <c>null</c> as "keep the shipped value", so the null cases get
    /// their own door.
    /// </summary>
    /// <remarks>
    /// 🔴 Every parameter is optional and passed BY NAME; a new one is appended LAST — a parameter
    /// inserted mid-signature merges textually clean and silently re-binds later positional
    /// arguments.
    /// </remarks>
    internal static RunSnapshot WithNull(
        bool streams = false,
        bool adUses = false,
        bool resolvedMinigames = false,
        bool startingLoadout = false) =>
        new(
            SnapshotSchema.SchemaVersion,
            Id,
            Owner,
            Seed,
            1,
            DifficultyTier.NORMAL,
            Midmorning,
            0,
            100,
            100,
            0L,
            streams ? null! : Streams(),
            adUses ? null! : AdUses(),
            resolvedMinigames ? null! : ResolvedMinigames(),
            PendingForkJunctionPosition: null,
            PendingForkRemainingSteps: null,
            NoPendingTile,
            0,
            0,
            NoPendingEventCard,
            StartingLoadout: startingLoadout ? null! : EmptyLoadout);

    /// <summary>The valid row with individual fields replaced. Omit a parameter to keep it.</summary>
    internal static RunSnapshot With(
        int? schemaVersion = null,
        RunId? id = null,
        PlayerId? playerId = null,
        ulong? runSeed = null,
        int? chapterId = null,
        DifficultyTier? tier = null,
        DateTimeOffset? lastAppliedAtUtc = null,
        int? position = null,
        int? currentHp = null,
        int? maxHp = null,
        long? gold = null,
        IReadOnlyDictionary<string, ulong>? rngStreamPositions = null,
        IReadOnlyDictionary<string, long>? adUses = null,
        IReadOnlyDictionary<int, string>? resolvedMinigames = null,
        int? pendingForkJunctionPosition = null,
        int? pendingForkRemainingSteps = null,
        int? pendingTileKind = null,
        int? pendingTileLinearIndex = null,
        int? pendingTileStage = null,
        string? pendingEventCardId = null,
        RunPhase? phase = null,
        bool? draftPending = null,
        int? draftBattleKind = null,
        int? draftBattleStage = null,
        IReadOnlyDictionary<string, int>? ownedPerkTiers = null,
        long? bankedLegendXp = null,
        long? bankedSoulShards = null,
        bool? bossDefeated = null,
        int? draftsSinceLegendaryOffered = null,
        int? draftsWithoutAboveCommon = null,
        int? draftsWithoutOwnedUpgrade = null,
        LoadoutSnapshot? startingLoadout = null,
        int? itemsAtOrAboveFloorBand = null,
        IReadOnlyList<string>? shrineBuffs = null,
        IReadOnlyList<string>? runBuffs = null,
        IReadOnlyList<string>? curses = null,
        IReadOnlyDictionary<string, int>? consumables = null,
        IReadOnlyDictionary<int, int>? fixedDice = null,
        int? pendingFixedDieChoices = null,
        bool? escapeRopeArmed = null,
        int? freeDraftRerolls = null,
        ulong? shopOfferDraw = null,
        int? shopSlotsPurchased = null,
        int? shopRefreshesUsedThisVisit = null) =>
        new(
            schemaVersion ?? SnapshotSchema.SchemaVersion,
            id ?? Id,
            playerId ?? Owner,
            runSeed ?? Seed,
            chapterId ?? 1,
            tier ?? DifficultyTier.NORMAL,
            lastAppliedAtUtc ?? Midmorning,
            position ?? 0,
            currentHp ?? 100,
            maxHp ?? 100,
            gold ?? 0L,
            rngStreamPositions ?? Streams(),
            adUses ?? AdUses(),
            resolvedMinigames ?? ResolvedMinigames(),
            pendingForkJunctionPosition,
            pendingForkRemainingSteps,
            pendingTileKind ?? NoPendingTile,
            pendingTileLinearIndex ?? 0,
            pendingTileStage ?? 0,
            pendingEventCardId ?? NoPendingEventCard,
            phase ?? RunPhase.InProgress,
            draftPending ?? false,
            draftBattleKind ?? NoDraftBattleKind,
            draftBattleStage ?? 0,
            ownedPerkTiers ?? OwnedPerkTiers(),
            bankedLegendXp ?? 0,
            bankedSoulShards ?? 0,
            bossDefeated ?? false,
            draftsSinceLegendaryOffered ?? 0,
            draftsWithoutAboveCommon ?? 0,
            draftsWithoutOwnedUpgrade ?? 0,
            startingLoadout ?? EmptyLoadout,
            itemsAtOrAboveFloorBand ?? 0,
            shrineBuffs ?? Ids(),
            runBuffs ?? Ids(),
            curses ?? Ids(),
            consumables ?? Consumables(),
            fixedDice ?? FixedDice(),
            pendingFixedDieChoices ?? 0,
            escapeRopeArmed ?? false,
            freeDraftRerolls ?? 0,
            shopOfferDraw,
            shopSlotsPurchased ?? 0,
            shopRefreshesUsedThisVisit ?? 0);

    /// <summary>
    /// An id list, empty by default — the shape <c>Run.ToSnapshot</c> writes for a run that has taken
    /// no shrine buff, bought no run buff and carries no curse.
    /// </summary>
    /// <remarks>
    /// Empty rather than <c>null</c>, and that is what keeps the byte round-trip honest: the
    /// aggregate always writes a list, so a fixture row holding <c>null</c> would encode differently
    /// from the row the same state produces after one trip through <c>Rehydrate</c>.
    /// </remarks>
    internal static IReadOnlyList<string> Ids(params string[] ids) =>
        ids.Length == 0 ? Array.Empty<string>() : ids;

    /// <inheritdoc cref="Ids"/>
    internal static IReadOnlyDictionary<int, int> FixedDice(params (int Pips, int Count)[] held) =>
        held.ToDictionary(h => h.Pips, h => h.Count);

    /// <inheritdoc cref="Ids"/>
    internal static IReadOnlyDictionary<string, int> Consumables(params (string Id, int Count)[] held) =>
        held.ToDictionary(h => h.Id, h => h.Count, StringComparer.Ordinal);

    /// <summary>
    /// Empty, never <c>null</c> — an absent starting loadout is a fault. Expression-bodied, which
    /// is load-bearing for <see cref="Valid"/>'s sake — see <c>PlayerSnapshots.EmptyInventory</c>.
    /// </summary>
    internal static LoadoutSnapshot EmptyLoadout =>
        new(new System.Collections.ObjectModel.ReadOnlyDictionary<GearSlot, GearInstanceId>(
            new Dictionary<GearSlot, GearInstanceId>(0)));

    /// <summary>
    /// <c>RunSnapshot.PendingTileKind</c>'s "no tile pending" sentinel, restated here because
    /// <c>Run</c>'s own constant is private, and this is a fixture writing a snapshot.
    /// </summary>
    internal const int NoPendingTile = -1;

    /// <summary><c>RunSnapshot.PendingEventCardId</c>'s "no card drawn" value.</summary>
    internal const string NoPendingEventCard = "";

    /// <summary>
    /// The valid row standing on an unresolved tile. The kind is an <c>int</c> because
    /// <c>Rules.Board.TileKind</c> is internal — the snapshot stores an <c>int</c> for the same
    /// accessibility reason.
    /// </summary>
    internal static RunSnapshot OnPendingTile(
        int tileKind, int linearIndex = 7, int stage = 1, string? eventCardId = null) =>
        With(
            pendingTileKind: tileKind,
            pendingTileLinearIndex: linearIndex,
            pendingTileStage: stage,
            pendingEventCardId: eventCardId ?? NoPendingEventCard);
}
