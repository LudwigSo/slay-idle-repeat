using Shouldly;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// <c>Run.ToSnapshot</c> and <c>Run.Rehydrate</c> as a pair, and the properties the persistence
/// and <c>stateHash</c> machinery depends on.
/// </summary>
public sealed class RunSnapshotTests
{
    /// <summary>A populated row: every field a valid row can move away from its default is moved.</summary>
    private static RunSnapshot Populated => RunSnapshots.With(
        runSeed: 0xFEEDFACECAFEBEEFUL,
        chapterId: 5,
        tier: DifficultyTier.MYTHIC,
        lastAppliedAtUtc: RunSnapshots.Midmorning.AddSeconds(4),
        position: 19,
        currentHp: 61,
        maxHp: 140,
        gold: 1_450,
        rngStreamPositions: RunSnapshots.Streams(
            (RngStreams.Dice, 12UL), (RngStreams.Board, 8UL), (RngStreams.Combat, 3UL)),
        adUses: RunSnapshots.AdUses(("AD_REVIVE", 1), ("AD_REROLL_DICE", 2)),
        resolvedMinigames: RunSnapshots.ResolvedMinigames((3, "MG_CHEST_PICK"), (11, "MG_TIMING_BAR")),
        pendingForkJunctionPosition: 19,
        pendingForkRemainingSteps: 2,
        rerollChargesSpentThisStage: 1,
        stageGateDiceAnchor: 5UL,
        ownedPerkTiers: RunSnapshots.OwnedPerkTiers(("PK_SHARP_EDGE", 2)),
        bankedLegendXp: 40,
        bankedSoulShards: 15,
        bossDefeated: true,
        draftsSinceLegendaryOffered: 1,
        draftsWithoutAboveCommon: 2,
        draftsWithoutOwnedUpgrade: 3,
        startingLoadout: new LoadoutSnapshot(
            new Dictionary<GearSlot, GearInstanceId> { [GearSlot.WEAPON] = new("GI_1") }),
        itemsAtOrAboveFloorBand: 2);

    /// <summary>
    /// The <c>stateHash</c> of a run command hashes the player snapshot then the run snapshot;
    /// the player half is held at <c>PlayerSnapshots.Valid</c> throughout this file, so every
    /// difference measured below is a difference in the run.
    /// </summary>
    private static string Hash(RunSnapshot run) =>
        CanonicalStateWriter.HashRunCommandState(PlayerSnapshots.Valid, run);

    /// <summary>
    /// Canonical bytes rather than a field list: a field list goes stale as the record grows, and
    /// the bytes cover every field the writer encodes — including one a constructor dropped.
    /// </summary>
    [Fact]
    public void A_snapshot_round_trips_byte_identically_through_the_aggregate()
    {
        var round = Run.Rehydrate(Populated).Value.ToSnapshot();

        CanonicalStateWriter.CanonicalBytes(round)
            .ShouldBe(CanonicalStateWriter.CanonicalBytes(Populated));
    }

    [Fact]
    public void A_stream_map_built_in_a_different_order_hashes_identically()
    {
        var forwards = RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams(
            (RngStreams.Board, 8UL), (RngStreams.Dice, 12UL)));

        var backwards = RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams(
            (RngStreams.Dice, 12UL), (RngStreams.Board, 8UL)));

        Hash(forwards)
            .ShouldBe(Hash(backwards));
    }

    /// <summary>Negative control: the case above must not be observing that everything hashes the same.</summary>
    [Fact]
    public void A_different_stream_position_hashes_differently()
    {
        var early = RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 12UL)));
        var late = RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 13UL)));

        Hash(early)
            .ShouldNotBe(Hash(late));
    }

    /// <summary>
    /// A snapshot field that does not move the <c>stateHash</c> is state two runs can differ in
    /// while the client-mirror check reports agreement. The probe set is floored against the
    /// record's constructor arity so a field added without a probe goes red here.
    /// </summary>
    [Fact]
    public void Every_field_of_the_snapshot_reaches_the_hash()
    {
        var v = RunSnapshots.Valid;

        var probes = new (string Field, RunSnapshot A, RunSnapshot B)[]
        {
            // An expression, not a literal — see PlayerSnapshotTests' probe of the same name.
            (nameof(RunSnapshot.SchemaVersion), v,
                RunSnapshots.With(schemaVersion: SnapshotSchema.SchemaVersion + 1)),
            (nameof(RunSnapshot.Id), v, RunSnapshots.With(id: new RunId("OTHER_RUN"))),
            (nameof(RunSnapshot.PlayerId), v, RunSnapshots.With(playerId: new PlayerId("OTHER_PLAYER"))),
            (nameof(RunSnapshot.RunSeed), v, RunSnapshots.With(runSeed: RunSnapshots.Seed + 1)),
            (nameof(RunSnapshot.ChapterId), v, RunSnapshots.With(chapterId: 2)),
            (nameof(RunSnapshot.Tier), v, RunSnapshots.With(tier: DifficultyTier.HEROIC)),
            (nameof(RunSnapshot.LastAppliedAtUtc), v, RunSnapshots.With(lastAppliedAtUtc: RunSnapshots.Midmorning.AddSeconds(1))),
            (nameof(RunSnapshot.Position), v, RunSnapshots.With(position: 1)),
            (nameof(RunSnapshot.CurrentHp), v, RunSnapshots.With(currentHp: 99)),
            (nameof(RunSnapshot.MaxHp), v, RunSnapshots.With(currentHp: 100, maxHp: 101)),
            (nameof(RunSnapshot.Gold), v, RunSnapshots.With(gold: 1)),
            (nameof(RunSnapshot.RngStreamPositions), v, RunSnapshots.With(rngStreamPositions: RunSnapshots.Streams((RngStreams.Dice, 1UL)))),
            (nameof(RunSnapshot.AdUses), v, RunSnapshots.With(adUses: RunSnapshots.AdUses(("AD_REVIVE", 1)))),
            (nameof(RunSnapshot.ResolvedMinigames), v,
                RunSnapshots.With(resolvedMinigames: RunSnapshots.ResolvedMinigames((0, "MG_CHEST_PICK")))),
            (nameof(RunSnapshot.PendingForkJunctionPosition), v,
                RunSnapshots.With(pendingForkJunctionPosition: 3)),
            (nameof(RunSnapshot.PendingForkRemainingSteps), v,
                RunSnapshots.With(pendingForkRemainingSteps: 1)),

            // The three tile-describing fields are probed TOGETHER WITH a pending kind: Rehydrate
            // refuses an index or stage with nothing pending, and a probe of a row no run can be
            // in proves nothing about the rows runs actually persist.
            (nameof(RunSnapshot.PendingTileKind), v,
                RunSnapshots.OnPendingTile((int)TileKind.Empty)),
            (nameof(RunSnapshot.PendingTileLinearIndex),
                RunSnapshots.OnPendingTile((int)TileKind.Empty, linearIndex: 7),
                RunSnapshots.OnPendingTile((int)TileKind.Empty, linearIndex: 8)),
            (nameof(RunSnapshot.PendingTileStage),
                RunSnapshots.OnPendingTile((int)TileKind.Empty, stage: 1),
                RunSnapshots.OnPendingTile((int)TileKind.Empty, stage: 2)),
            (nameof(RunSnapshot.PendingEventCardId),
                RunSnapshots.OnPendingTile((int)TileKind.Event),
                RunSnapshots.OnPendingTile((int)TileKind.Event, eventCardId: "EVT_WELL")),

            (nameof(RunSnapshot.Phase), v, RunSnapshots.With(phase: RunPhase.BattlePending)),
            (nameof(RunSnapshot.DraftPending), v, RunSnapshots.With(draftPending: true)),
            (nameof(RunSnapshot.RerollChargesSpentThisStage), v,
                RunSnapshots.With(rerollChargesSpentThisStage: 1)),
            (nameof(RunSnapshot.StageGateDiceAnchor), v, RunSnapshots.With(stageGateDiceAnchor: 5UL)),

            // Probed together with DraftPending true, on the pending-tile probes' precedent:
            // Rehydrate refuses a kind or stage with no draft pending.
            (nameof(RunSnapshot.DraftBattleKind),
                RunSnapshots.With(draftPending: true, draftBattleKind: (int)TileKind.Enemy, draftBattleStage: 1),
                RunSnapshots.With(draftPending: true, draftBattleKind: (int)TileKind.Elite, draftBattleStage: 1)),
            (nameof(RunSnapshot.DraftBattleStage),
                RunSnapshots.With(draftPending: true, draftBattleKind: (int)TileKind.Enemy, draftBattleStage: 1),
                RunSnapshots.With(draftPending: true, draftBattleKind: (int)TileKind.Enemy, draftBattleStage: 2)),
            (nameof(RunSnapshot.OwnedPerkTiers), v,
                RunSnapshots.With(ownedPerkTiers: RunSnapshots.OwnedPerkTiers(("PK_SHARP_EDGE", 1)))),

            (nameof(RunSnapshot.BankedLegendXp), v, RunSnapshots.With(bankedLegendXp: 5L)),
            (nameof(RunSnapshot.BankedSoulShards), v, RunSnapshots.With(bankedSoulShards: 5L)),
            (nameof(RunSnapshot.BossDefeated), v, RunSnapshots.With(bossDefeated: true)),

            // Probed separately: the three counters move independently, and a writer that reached
            // only the first would be invisible to a combined probe.
            (nameof(RunSnapshot.DraftsSinceLegendaryOffered), v,
                RunSnapshots.With(draftsSinceLegendaryOffered: 1)),
            (nameof(RunSnapshot.DraftsWithoutAboveCommon), v,
                RunSnapshots.With(draftsWithoutAboveCommon: 1)),
            (nameof(RunSnapshot.DraftsWithoutOwnedUpgrade), v,
                RunSnapshots.With(draftsWithoutOwnedUpgrade: 1)),

            // 07 §4 freezes the loadout at run start: written once, by START_RUN, and never touched
            // again — exactly the shape an encoder can silently skip.
            (nameof(RunSnapshot.StartingLoadout), v,
                RunSnapshots.With(startingLoadout: new LoadoutSnapshot(
                    new Dictionary<GearSlot, GearInstanceId> { [GearSlot.WEAPON] = new("GI_1") }))),

            // 24 §4.3's session floor: written only by a drop that reached the band — a rarely
            // taken path an encoder can skip without anything else noticing.
            (nameof(RunSnapshot.ItemsAtOrAboveFloorBand), v,
                RunSnapshots.With(itemsAtOrAboveFloorBand: 1)),

            // ------------------------------------------------ the run-tile state
            //
            // Every field a board tile writes. Probed one at a time rather than as a block: they are
            // written by six different tiles, and a combined probe would go green on any one of them
            // reaching the encoder.
            (nameof(RunSnapshot.ShrineBuffs), v, RunSnapshots.With(shrineBuffs: RunSnapshots.Ids("SHR_ATK"))),
            (nameof(RunSnapshot.RunBuffs), v, RunSnapshots.With(runBuffs: RunSnapshots.Ids("WHETSTONE"))),
            (nameof(RunSnapshot.Curses), v, RunSnapshots.With(curses: RunSnapshots.Ids("CUR_FRACTURED"))),
            (nameof(RunSnapshot.DieFaceUpgrades), v,
                RunSnapshots.With(dieFaceUpgrades: RunSnapshots.DieFaceUpgrades((1, 42)))),
            (nameof(RunSnapshot.Consumables), v,
                RunSnapshots.With(consumables: RunSnapshots.Consumables(("CON_HEALTH_DRAUGHT", 1)))),
            (nameof(RunSnapshot.EscapeRopeArmed), v, RunSnapshots.With(escapeRopeArmed: true)),
            (nameof(RunSnapshot.RerollChargesGrantedThisStage), v,
                RunSnapshots.With(rerollChargesGrantedThisStage: 2)),
            (nameof(RunSnapshot.FreeDraftRerolls), v, RunSnapshots.With(freeDraftRerolls: 1)),

            // The shop trio is probed against an OPEN shop for the pending-tile probes' reason:
            // Rehydrate refuses purchases or refreshes recorded while no offer is open, so a probe
            // of a closed shop carrying either would be a row no run can persist.
            (nameof(RunSnapshot.ShopOfferDraw), v, RunSnapshots.With(shopOfferDraw: 3UL)),
            (nameof(RunSnapshot.ShopSlotsPurchased),
                RunSnapshots.With(shopOfferDraw: 3UL),
                RunSnapshots.With(shopOfferDraw: 3UL, shopSlotsPurchased: 1)),
            (nameof(RunSnapshot.ShopRefreshesUsedThisVisit),
                RunSnapshots.With(shopOfferDraw: 3UL),
                RunSnapshots.With(shopOfferDraw: 3UL, shopRefreshesUsedThisVisit: 1)),

            // A roll sequence mid-chain. Persisted because a Chain hop resolves its landing tile in
            // full before the next chained roll, so the sequence spans several commands.
            (nameof(RunSnapshot.ChainLinksTaken), v, RunSnapshots.With(chainLinksTaken: 1)),
        };

        var invisible = probes
            .Where(p => Hash(p.A)
                        == Hash(p.B))
            .Select(p => p.Field)
            .ToArray();

        invisible.ShouldBeEmpty(
            "a snapshot field that does not move the stateHash is state two runs can differ in " +
            "while 14 §2.4's client-mirror check reports agreement.");

        probes.Select(p => p.Field).Distinct(StringComparer.Ordinal).Count()
            .ShouldBe(typeof(RunSnapshot).GetConstructors().Single().GetParameters().Length);
    }

    /// <summary>Mutating the aggregate must not rewrite a row already handed to a persistence adapter.</summary>
    [Fact]
    public void A_snapshot_taken_before_a_mutation_is_not_changed_by_it()
    {
        var run = Run.Rehydrate(Populated).Value;
        var before = run.ToSnapshot();
        var hashBefore = Hash(before);

        run.MoveCurrency(CurrencyId.GOLD, 300, "tile_kill_gold");
        run.CountAdUse("AD_REVIVE", 1);
        run.MoveTo(20);
        run.CommitStreamPositions(new Dictionary<string, ulong>(StringComparer.Ordinal)
        {
            [RngStreams.Dice] = 13,
            [RngStreams.Board] = 8,
            [RngStreams.Combat] = 4,
        });

        before.Gold.ShouldBe(1_450);
        before.AdUses["AD_REVIVE"].ShouldBe(1);
        before.Position.ShouldBe(19);
        before.RngStreamPositions[RngStreams.Dice].ShouldBe(12UL);
        Hash(before).ShouldBe(hashBefore);
    }

    /// <summary>A snapshot describes today's layout, whatever the row it was rehydrated from said.</summary>
    [Fact]
    public void ToSnapshot_stamps_the_current_SchemaVersion()
    {
        Run.Rehydrate(RunSnapshots.Valid).Value
           .ToSnapshot().SchemaVersion.ShouldBe(SnapshotSchema.SchemaVersion);
    }
}
