using System.Collections.ObjectModel;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Tests.Model;

// Namespace SlayIdleRepeat.Core.Tests.Model, not ...Tests.Model.Run, and the files still sit
// under Model/Run/. Same reason the production aggregate does it: a child namespace named `Run`
// shadows the type `Run` for everything inside SlayIdleRepeat.Core.Tests.Model, so a test written
// here could not name the very class it is testing (CS0118).

/// <summary>
/// Hermetic <see cref="RunSnapshot"/> fixtures — one valid row, and a <c>With(...)</c> that replaces
/// exactly one field so a test names the single thing it is about.
/// </summary>
/// <remarks>
/// A test that built a whole snapshot inline would restate every field to change one, and the
/// reader could not tell which one it was asserting about.
/// <para>
/// The baseline numbers are test values and carry <b>no design claim</b>: legal and
/// unremarkable, not a starting state.
/// </para>
/// </remarks>
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
    /// The valid row with one of its two reference-typed maps replaced by <c>null</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="With"/> cannot express this: its optional parameters read <c>null</c> as "keep the
    /// shipped value", which is what makes it readable — so the null cases get their own door rather
    /// than a sentinel every other call site would have to understand.
    /// </remarks>
    /// <remarks>
    /// 🔴 Every parameter here is optional and every call site passes them BY NAME. A new one is
    /// appended LAST and nowhere else: a parameter inserted mid-signature merges textually clean and
    /// silently re-binds every positional argument after it.
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
        int? rerollChargesSpentThisStage = null,
        ulong? stageGateDiceAnchor = null,
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
        int? itemsAtOrAboveFloorBand = null) =>
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
            rerollChargesSpentThisStage ?? 0,
            stageGateDiceAnchor ?? 0,
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
            itemsAtOrAboveFloorBand ?? 0);

    /// <summary>A hero wearing nothing — where a run started by a player with no gear begins.</summary>
    /// <remarks>
    /// Empty, never <c>null</c>: an absent starting loadout is a fault on the player inventory's
    /// precedent, so a fixture defaulting to one would make every rehydration case in this suite fail
    /// for a reason unrelated to what it asserts. Expression-bodied rather than an initialised static,
    /// which is load-bearing for <see cref="Valid"/>'s sake — see <c>PlayerSnapshots.EmptyInventory</c>.
    /// </remarks>
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
    /// The valid row standing on an unresolved tile of <paramref name="tileKind"/>.
    /// </summary>
    /// <remarks>
    /// Takes the tile kind as an <c>int</c> rather than a <c>TileKind</c>, because
    /// <c>Rules.Board.TileKind</c> is <c>internal</c> to <c>Core</c> and reachable from the test
    /// assembly only through <c>InternalsVisibleTo</c> — the snapshot itself stores an <c>int</c>
    /// for the same accessibility reason, so the fixture mirrors the row.
    /// </remarks>
    internal static RunSnapshot OnPendingTile(
        int tileKind, int linearIndex = 7, int stage = 1, string? eventCardId = null) =>
        With(
            pendingTileKind: tileKind,
            pendingTileLinearIndex: linearIndex,
            pendingTileStage: stage,
            pendingEventCardId: eventCardId ?? NoPendingEventCard);
}
