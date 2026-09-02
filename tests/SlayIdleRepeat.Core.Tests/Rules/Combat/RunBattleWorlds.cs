using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Tests.TestSupport;
using SlayIdleRepeat.Core.Tests.Model;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using PlayerAggregate = SlayIdleRepeat.Core.Model.Player;
using RunAggregate = SlayIdleRepeat.Core.Model.Run;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// A geared player standing in an open battle, over the shipped content set — the fixture the
/// run-to-fight composition is stated against.
/// </summary>
/// <remarks>
/// The shipped content rather than a stitched one, because the composition reads nine documents at
/// once: the par table, the gear catalogue, the drop tables, the forge ladder, the set breakpoints,
/// the combat caps, the enemy catalogue, the boss catalogue and the chapter's board config. A
/// fixture set assembled here would have to author every one of them, and the first missing pointer
/// would fail every case for a reason none of them is about.
/// </remarks>
internal static class RunBattleWorlds
{
    /// <summary>The shipped <c>game-data</c>, loaded once.</summary>
    internal static ContentSnapshot Content => ShippedHarness.Content;

    /// <summary>The Legend Level every fixture hero stands at.</summary>
    internal const int LegendLevel = 20;

    /// <summary>The committed run seed. Fixed, so every hash assertion here is reproducible.</summary>
    internal const ulong RunSeed = 0x5EED_B0A7_0000_0011UL;

    /// <summary>How many battles the fixture run has already started, itself included.</summary>
    /// <remarks>
    /// The <c>combat</c> counter counts battles STARTED, so a run inside its first battle carries
    /// one and that battle's index is zero. Two here, so a case that reads the counter instead of
    /// the index — or the index instead of the counter — lands on a different seed rather than the
    /// same one by coincidence.
    /// </remarks>
    internal const ulong BattlesStarted = 2UL;

    /// <summary>The full six-slot Bloodmoon loadout at SS, one item per slot.</summary>
    /// <remarks>
    /// A whole set rather than one item: the composition has to carry the item's own two stats, the
    /// enhancement ladder, the affix rolls and the set breakpoints, and a single-slot fixture would
    /// leave three of those four contributing nothing to any assertion here.
    /// </remarks>
    internal static IReadOnlyList<GearInstance> Worn { get; } = new[]
    {
        GearFamily.BLADE,
        GearFamily.LEATHERS,
        GearFamily.HOOD,
        GearFamily.TREADS,
        GearFamily.BAND,
        GearFamily.PENDANT,
    }
    .Select((family, index) => Inventories.Item(
        "worn_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        family,
        Rarity.SS,
        enhanceLevel: 5,
        // Only the blade: AFX_ATTACK_SPEED's authored slot list is WEAPON and BOOTS, and an affix
        // rolled onto a slot the table does not offer it on is not a loadout the game can produce.
        affixes: family == GearFamily.BLADE ? [new GearAffixRoll("AFX_ATTACK_SPEED", 0.05)] : null))
    .ToArray();

    /// <summary>
    /// The same six slots, deliberately far ABOVE chapter-1 par: top band, top chapter of origin,
    /// perfect quality, top enhance level.
    /// </summary>
    /// <remarks>
    /// For fixtures that must win a whole RUN, not one fight: <see cref="Worn"/> lands a chapter-1
    /// hero roughly AT par, and enemy power grows 3.5% per node, so an at-par hero dies mid-run.
    /// Over-geared by a wide margin rather than a tuned one, so no retune leaves a run fixture
    /// silently asserting that a dead run banks nothing. A SECOND loadout rather than a change to
    /// <see cref="Worn"/>, whose composed figures other cases pin literally.
    /// </remarks>
    internal static IReadOnlyList<GearInstance> FarAbovePar { get; } = new[]
    {
        GearFamily.BLADE,
        GearFamily.LEATHERS,
        GearFamily.HOOD,
        GearFamily.TREADS,
        GearFamily.BAND,
        GearFamily.PENDANT,
    }
    .Select((family, index) => Inventories.Item(
        "above_par_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
        family,
        Rarity.SS,
        chapterOrigin: TopChapter,
        quality: 1.0,
        enhanceLevel: TopEnhanceLevel))
    .ToArray();

    /// <summary>The loadout row naming <see cref="FarAbovePar"/>, slot by slot.</summary>
    internal static LoadoutSnapshot FarAboveParLoadout { get; } = new(
        new System.Collections.ObjectModel.ReadOnlyDictionary<GearSlot, GearInstanceId>(
            FarAbovePar.ToDictionary(item => item.Slot, item => item.InstanceId)));

    /// <summary>The last chapter the game authors, so item power is scaled as high as `08` §3 goes.</summary>
    private const int TopChapter = 8;

    /// <summary>`08` §4.2's enhance ceiling.</summary>
    private const int TopEnhanceLevel = 15;

    /// <summary>The loadout row naming <see cref="Worn"/>, slot by slot.</summary>
    internal static LoadoutSnapshot WornLoadout { get; } = new(
        new System.Collections.ObjectModel.ReadOnlyDictionary<GearSlot, GearInstanceId>(
            Worn.ToDictionary(item => item.Slot, item => item.InstanceId)));

    /// <summary>A loadout naming nothing — the hero who fights on the base curve alone.</summary>
    internal static LoadoutSnapshot BareLoadout { get; } = new(
        new System.Collections.ObjectModel.ReadOnlyDictionary<GearSlot, GearInstanceId>(
            new Dictionary<GearSlot, GearInstanceId>(0)));

    /// <summary>The player row: <see cref="LegendLevel"/>, holding and wearing <see cref="Worn"/>.</summary>
    internal static PlayerSnapshot PlayerRow(bool geared = true) => PlayerSnapshots.With(
        legendLevel: LegendLevel,
        inventory: new InventorySnapshot(0, Worn.Select(Inventories.Persist).ToArray(), []),
        loadout: geared ? WornLoadout : BareLoadout);

    /// <summary>
    /// The player row for <see cref="FarAbovePar"/>: holding and wearing the over-par six.
    /// </summary>
    /// <param name="clearedChapterTiers">
    /// The chapter/tier clears this account already holds, for a case about a repeat clear. The
    /// parameter exists because a case that built its own row to add them would drop the gear with it —
    /// and a bare-handed hero loses the boss fight the case is about.
    /// </param>
    internal static PlayerSnapshot FarAboveParRow(
        IReadOnlyDictionary<string, long>? clearedChapterTiers = null) => PlayerSnapshots.With(
        legendLevel: LegendLevel,
        inventory: new InventorySnapshot(0, FarAbovePar.Select(Inventories.Persist).ToArray(), []),
        loadout: FarAboveParLoadout,
        clearedChapterTiers: clearedChapterTiers);

    /// <summary>The run row: standing in an open battle against <paramref name="kind"/>.</summary>
    /// <param name="kind">The pending tile the fight is against.</param>
    /// <param name="stage">The stage the tile belongs to — the boss's is <see cref="BoardGraph.BossStage"/>.</param>
    /// <param name="linearIndex">The tile's linear node index, which the power curve scales on.</param>
    /// <param name="chapter">The chapter.</param>
    /// <param name="tier">The difficulty tier.</param>
    /// <param name="phase">The run phase. <see cref="RunPhase.BattlePending"/> unless a refusal case says otherwise.</param>
    /// <param name="battlesStarted">The <c>combat</c> stream counter.</param>
    /// <param name="geared">Whether the run froze the geared loadout or the bare one.</param>
    internal static RunSnapshot RunRow(
        TileKind kind = TileKind.Enemy,
        int stage = 1,
        int linearIndex = 7,
        int chapter = 1,
        DifficultyTier tier = DifficultyTier.NORMAL,
        RunPhase phase = RunPhase.BattlePending,
        ulong battlesStarted = BattlesStarted,
        bool geared = true) => RunSnapshots.With(
        runSeed: RunSeed,
        chapterId: chapter,
        tier: tier,
        pendingTileKind: (int)kind,
        pendingTileLinearIndex: linearIndex,
        pendingTileStage: stage,
        phase: phase,
        rngStreamPositions: RunSnapshots.Streams((RngStreams.Combat, battlesStarted)),
        startingLoadout: geared ? WornLoadout : BareLoadout);

    /// <summary>A run row standing on no tile at all.</summary>
    internal static RunSnapshot OnNoTileRow(RunPhase phase = RunPhase.BattlePending) => RunSnapshots.With(
        runSeed: RunSeed,
        phase: phase,
        rngStreamPositions: RunSnapshots.Streams((RngStreams.Combat, BattlesStarted)),
        startingLoadout: WornLoadout);

    /// <summary>The player aggregate for a row.</summary>
    internal static PlayerAggregate Player(PlayerSnapshot? row = null)
    {
        var player = PlayerAggregate.Rehydrate(row ?? PlayerRow(), Content);

        return player.IsSuccess
            ? player.Value
            : throw new InvalidOperationException(
                "The fixture PlayerSnapshot does not rehydrate: " + player.Error);
    }

    /// <summary>The run aggregate for a row.</summary>
    internal static RunAggregate Run(RunSnapshot? row = null)
    {
        var run = RunAggregate.Rehydrate(row ?? RunRow());

        return run.IsSuccess
            ? run.Value
            : throw new InvalidOperationException(
                "The fixture RunSnapshot does not rehydrate: " + run.Error);
    }
}
