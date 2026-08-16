using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Handlers;

/// <summary>
/// <see cref="WorldSlice"/> and <see cref="GameContext"/> fixtures for the in-run gear grant paths:
/// a run standing in an open battle over a kill tile, and the run-end shapes the session floor reads.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The content is the whole shipped set, not <c>TileWorlds</c>'.</b> A gear grant reads the
/// catalogue, the drop tables, the par curve, the pity registry and the inventory capacity, and the
/// tile suite's content set carries none of the five — a fixture short of one of them would fail
/// inside the reader rather than in the rule under test.
/// </para>
/// <para>
/// 🔴 <b>The stream positions are searched for, not guessed.</b> Whether an ordinary kill drops
/// anything, and which band an Elite drop lands on, are both decisions of a single draw at the run's
/// committed <c>drops</c> position — so a case that wants one side of that decision asks
/// <see cref="NormalEnemyDrawPosition"/> or <see cref="EliteDrawPosition"/> for a position that
/// produces it, off the production rule itself rather than off a re-derivation of it.
/// </para>
/// </remarks>
internal static class GearGrantWorlds
{
    /// <summary>The committed run seed every fixture uses. <c>RunSnapshots.Seed</c>, so the drop stream a
    /// search walks and the one a world commits are the same sequence.</summary>
    internal const ulong Seed = RunSnapshots.Seed;

    /// <summary>How far a stream search looks before giving up rather than looping forever.</summary>
    private const ulong SearchBound = 4096UL;

    /// <summary>The linear node the kill tile sits on. <c>TileWorlds</c>' own, so the two agree.</summary>
    private const int KillNode = 7;

    /// <summary>The instant every fixture applies at — <c>Worlds.NowUtc</c>, so the two agree.</summary>
    internal static readonly DateTimeOffset NowUtc = Worlds.NowUtc;

    /// <summary>The whole shipped content set, off the real <c>game-data/</c> tree.</summary>
    internal static ContentSnapshot Content => ShippedHarness.Content;

    /// <summary>A context at <see cref="NowUtc"/> over <see cref="Content"/>, with no command seed.</summary>
    internal static GameContext Context { get; } = new(
        NowUtc,
        CommandSeed: null,
        ShippedHarness.Content,
        TestSupport.GameContexts.WithoutPlus,
        TestSupport.GameContexts.NoKillSwitchThrown);

    /// <summary>The in-run drop protections the shipped set authors.</summary>
    internal static DropRunTuning DropRun { get; } = DropRunTuning.Read(ShippedHarness.Content);

    /// <summary>The pity registry — also the one place a counter id is formed.</summary>
    internal static LuckTuning Luck { get; } = LuckTuning.Read(ShippedHarness.Content);

    /// <summary>The gear tables a drop's band is drawn against.</summary>
    internal static DropsTuning Drops { get; } = DropsTuning.Read(ShippedHarness.Content);

    /// <summary>The inventory numbers, for the capacity an overflow case has to actually reach.</summary>
    internal static InventoryTuning Stock { get; } = InventoryTuning.Read(ShippedHarness.Content);

    /// <summary>The authored acquisition rates.</summary>
    /// <remarks>
    /// A method rather than an initialised property: a reader that throws would otherwise take out
    /// every case in the suite as a type-initialisation failure rather than the ones that read a rate.
    /// </remarks>
    internal static GearAcquisitionTuning Rates() => GearAcquisitionTuning.Read(Content);

    /// <summary>The counter the Elite dry-streak breaker is keyed by.</summary>
    internal static string EliteCounterKey { get; } =
        Luck.CounterKey(SourceClass.DROP_RUN, DropRun.EliteMercy.ForceRarityAtLeast);

    /// <summary>The counter the Boss dry-streak breaker is keyed by.</summary>
    internal static string BossCounterKey { get; } =
        Luck.CounterKey(SourceClass.DROP_RUN, DropRun.BossMercy.ForceRarityAtLeast);

    /// <summary>A pity map standing at <paramref name="misses"/> Elite misses.</summary>
    internal static IReadOnlyDictionary<string, int> EliteMercyAt(int misses) =>
        PlayerSnapshots.Pity((EliteCounterKey, misses));

    /// <summary>A pity map standing at <paramref name="misses"/> Boss misses.</summary>
    internal static IReadOnlyDictionary<string, int> BossMercyAt(int misses) =>
        PlayerSnapshots.Pity((BossCounterKey, misses));

    /// <summary>The counters an <see cref="EliteDrawPosition"/> search resolves against.</summary>
    internal static PityCounters Counters(IReadOnlyDictionary<string, int>? pity) =>
        pity is null ? PityCounters.Empty : PityCounters.Rehydrate(pity);

    /// <summary>
    /// A slice whose run has an open battle over a kill tile of <paramref name="kind"/>.
    /// </summary>
    /// <param name="kind">Enemy, Elite or Boss — the three kinds a kill can drop from.</param>
    /// <param name="inventory">The stock the player starts holding, or <c>null</c> for an empty one.</param>
    /// <param name="dropsPosition">Where the run's <c>drops</c> stream stands.</param>
    /// <param name="pity">The player's dry-streak counters, or <c>null</c> for a clean slate.</param>
    /// <param name="chapterId">The chapter the drop is scaled against.</param>
    /// <param name="stage">The stage the kill tile belongs to.</param>
    /// <param name="runId">The run's identity. Distinct per run when a case drives more than one.</param>
    internal static WorldSlice OnKill(
        TileKind kind,
        InventorySnapshot? inventory = null,
        ulong dropsPosition = 0UL,
        IReadOnlyDictionary<string, int>? pity = null,
        int chapterId = 1,
        int stage = 1,
        string? runId = null) =>
        new(
            Worlds.Rehydrated(PlayerSnapshots.With(inventory: inventory, pityCounters: pity)),
            Rehydrated(RunSnapshots.With(
                id: runId is null ? null : new RunId(runId),
                runSeed: Seed,
                chapterId: chapterId,
                lastAppliedAtUtc: NowUtc,
                rngStreamPositions: RunSnapshots.Streams((RngStreams.Drops, dropsPosition)),
                pendingTileKind: (int)kind,
                pendingTileLinearIndex: KillNode,
                pendingTileStage: stage,
                phase: RunPhase.BattlePending)));

    /// <summary>The same slice with a fresh battle open over another kill tile of the same kind.</summary>
    /// <remarks>
    /// The run's own state — its drop-stream position, its counters, its banked rewards — is carried
    /// through the snapshot it just wrote rather than rebuilt, which is what makes a sequence of kills
    /// one run rather than several.
    /// </remarks>
    internal static WorldSlice ReArmed(WorldSlice state, TileKind kind, int node)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new WorldSlice(state.Player, Rehydrated(state.Run!.ToSnapshot() with
        {
            Phase = RunPhase.BattlePending,
            PendingTileKind = (int)kind,
            PendingTileLinearIndex = node,
            PendingTileStage = 1,
            DraftPending = false,
            DraftBattleKind = RunSnapshots.NoDraftBattleKind,
            DraftBattleStage = 0,
        }));
    }

    /// <summary>The same slice with its run dead on a tile of <paramref name="stage"/>, ready for END_RUN.</summary>
    internal static WorldSlice DyingAtStage(WorldSlice state, int stage)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new WorldSlice(state.Player, Rehydrated(state.Run!.ToSnapshot() with
        {
            Phase = RunPhase.InProgress,
            CurrentHp = 0,
            PendingTileKind = (int)TileKind.Enemy,
            PendingTileLinearIndex = KillNode,
            PendingTileStage = stage,
            DraftPending = false,
            DraftBattleKind = RunSnapshots.NoDraftBattleKind,
            DraftBattleStage = 0,
        }));
    }

    /// <summary>The same slice with its run ended, so no loadout rule can be blaming a live run.</summary>
    internal static WorldSlice WithEndedRun(WorldSlice state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new WorldSlice(
            state.Player, Rehydrated(state.Run!.ToSnapshot() with { Phase = RunPhase.Ended }));
    }

    /// <summary>A run in progress, dead on a tile of <paramref name="stage"/>, under its own identity.</summary>
    internal static WorldSlice DeadAtStage(Player player, int stage, string runId)
    {
        ArgumentNullException.ThrowIfNull(player);

        return new WorldSlice(
            player,
            Rehydrated(RunSnapshots.With(
                id: new RunId(runId),
                runSeed: Seed,
                lastAppliedAtUtc: NowUtc,
                currentHp: 0,
                pendingTileKind: (int)TileKind.Enemy,
                pendingTileLinearIndex: KillNode,
                pendingTileStage: stage,
                phase: RunPhase.InProgress)));
    }

    /// <summary>A stock filled to the capacity an unexpanded inventory has, so the next grant overflows.</summary>
    internal static InventorySnapshot FullStock() =>
        new(0, Inventories.Fill(Stock.CapacityAt(0)).Select(Inventories.Persist).ToArray(), []);

    /// <summary>
    /// The first <c>drops</c> position whose draw puts an ordinary kill on the side of
    /// <paramref name="chance"/> that <paramref name="drops"/> asks for.
    /// </summary>
    /// <param name="drops">Whether the caller wants the position to produce a drop.</param>
    /// <param name="chance">The authored per-kill chance.</param>
    /// <returns>The position to commit the run's drop stream at.</returns>
    internal static ulong NormalEnemyDrawPosition(bool drops, double chance)
    {
        for (var position = 0UL; position < SearchBound; position++)
        {
            if (DeterministicRng.OpenAt(Seed, RngStreams.Drops, position).NextDouble() < chance == drops)
            {
                return position;
            }
        }

        throw new InvalidOperationException(
            "No drops-stream position under " + Text(SearchBound) + " draws " +
            (drops ? "under " : "at or above ") + chance.ToString(CultureInfo.InvariantCulture) +
            ". A search that finds nothing means the fixture is asking for a side of a probability " +
            "the stream never lands on, and every case built on it would be asserting the wrong arm.");
    }

    /// <summary>
    /// The first <c>drops</c> position at which an Elite kill's band resolution satisfies
    /// <paramref name="band"/>, decided by the façade rather than by a re-derivation of it.
    /// </summary>
    /// <param name="band">The predicate the resolved band has to satisfy.</param>
    /// <param name="pity">The counters the resolution is taken against.</param>
    /// <param name="chapterId">The chapter whose shares are drawn.</param>
    /// <returns>The position to commit the run's drop stream at.</returns>
    internal static ulong EliteDrawPosition(
        Func<Rarity, bool> band, IReadOnlyDictionary<string, int>? pity = null, int chapterId = 1)
    {
        ArgumentNullException.ThrowIfNull(band);

        var counters = Counters(pity);

        for (var position = 0UL; position < SearchBound; position++)
        {
            var resolved = LuckService.ResolveRunDrop(
                Luck,
                DropRun,
                Drops,
                chapterId,
                RunDropTrigger.ELITE,
                counters,
                DeterministicRng.OpenAt(Seed, RngStreams.Drops, position));

            if (band(resolved.Outcome))
            {
                return position;
            }
        }

        throw new InvalidOperationException(
            "No drops-stream position under " + Text(SearchBound) + " resolves an Elite drop the " +
            "caller's predicate accepts, so the case built on it would be asserting the wrong arm.");
    }

    /// <summary>Every identity the player owns, stored or held.</summary>
    internal static IReadOnlyList<GearInstanceId> Owned(Player player)
    {
        ArgumentNullException.ThrowIfNull(player);

        return player.Inventory.Stored
            .Concat(player.Inventory.Held)
            .Select(item => item.InstanceId)
            .ToArray();
    }

    /// <summary>Where a run's <c>drops</c> stream stands, reading an absent row as draw zero.</summary>
    internal static ulong DropsPositionOf(RunAggregate run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return run.ToSnapshot().RngStreamPositions.TryGetValue(RngStreams.Drops, out var position)
            ? position
            : 0UL;
    }

    private static RunAggregate Rehydrated(RunSnapshot snapshot)
    {
        var run = RunAggregate.Rehydrate(snapshot);

        return run.IsSuccess
            ? run.Value
            : throw new InvalidOperationException("The fixture RunSnapshot does not rehydrate: " + run.Error);
    }

    private static string Text(ulong value) => value.ToString(CultureInfo.InvariantCulture);
}
