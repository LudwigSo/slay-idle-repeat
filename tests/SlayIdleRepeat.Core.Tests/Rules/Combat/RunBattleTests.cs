using Shouldly;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Board;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// The seam that turns a run standing in an open battle into a fight: the hero it composes, the
/// enemy it composes, the seed it fights at, and what it refuses.
/// </summary>
/// <remarks>
/// Every case is stated so it fails if a single link is dropped: the loadout, the aggregation
/// input, the tile's power, the tile's kind, or the battle index the seed is derived from.
/// </remarks>
public sealed class RunBattleTests
{
    // ══════════════════════════════════════════════════════════════════ the seed

    /// <summary>
    /// The battle a run is standing in is the one BEFORE its combat counter, and the seed is that
    /// index's.
    /// </summary>
    /// <remarks>
    /// An off-by-one here is not a crash: it is a real seed for the wrong fight, which the server
    /// would recompute differently from the client and refuse. The assertion names the derivation
    /// rather than a literal so the two spellings of it cannot drift apart.
    /// </remarks>
    [Fact]
    public void The_open_battles_seed_is_the_index_before_the_runs_combat_counter()
    {
        RunBattle.SeedOf(RunBattleWorlds.RunRow()).ShouldBe(
            SeedDerivation.BattleSeed(RunBattleWorlds.RunSeed, (int)RunBattleWorlds.BattlesStarted - 1),
            "the combat stream counts battles started, so the one now open is the previous index");
    }

    /// <summary>The counter and the index are different numbers, so reading the wrong one is visible.</summary>
    /// <remarks>
    /// The negative control for the case above. Without it, a fixture whose counter happened to be
    /// one would make <c>counter</c> and <c>counter − 1</c> agree at the only value tested and the
    /// off-by-one would be invisible.
    /// </remarks>
    [Fact]
    public void The_seed_is_not_the_one_the_raw_counter_would_name()
    {
        RunBattle.SeedOf(RunBattleWorlds.RunRow()).ShouldNotBe(
            SeedDerivation.BattleSeed(RunBattleWorlds.RunSeed, (int)RunBattleWorlds.BattlesStarted),
            "the raw counter names the NEXT battle, not the open one");
    }

    /// <summary>The snapshot door and the aggregate door derive the same seed.</summary>
    /// <remarks>
    /// The whole reason the seam has two doors and one derivation: the client reaches it through a
    /// snapshot and the confirming handler through the aggregate, and a fight the two disagree about
    /// is a fight the server refuses.
    /// </remarks>
    [Fact]
    public void Both_doors_derive_the_same_seed()
    {
        var row = RunBattleWorlds.RunRow();

        RunBattle.SeedOf(RunBattleWorlds.Run(row)).ShouldBe(RunBattle.SeedOf(row));
    }

    /// <summary>A run whose combat stream was never drawn from names no open battle.</summary>
    [Fact]
    public void A_run_whose_combat_stream_never_advanced_has_no_battle_to_name()
    {
        var refused = Should.Throw<InvalidOperationException>(
            () => RunBattle.SeedOf(RunBattleWorlds.RunRow(battlesStarted: 0UL)));

        // "combat" alone would not do: the phase refusal names the combat counter too, so the
        // assertion has to be a phrase only the never-drawn rule uses.
        refused.Message.ShouldContain(
            "never been drawn from",
            Case.Insensitive,
            "the refusal has to name the counter that is missing, not the phase or the tile");
    }

    // ══════════════════════════════════════════════════════════════════ the fight

    /// <summary>Each of the three fight tiles composes into a fight that actually ran.</summary>
    /// <remarks>
    /// A log with events and a non-zero duration, because an empty log is what a composition that
    /// silently produced no roster hands back — and it is indistinguishable from a fight the hero
    /// lost on the first tick if only the outcome is asserted.
    /// </remarks>
    [Theory]
    [InlineData((int)TileKind.Enemy, 1)]
    [InlineData((int)TileKind.Elite, 2)]
    [InlineData((int)TileKind.Boss, BoardGraph.BossStage)]
    public void Each_fight_tile_composes_into_a_fight_that_ran(int kind, int stage)
    {
        var result = RunBattle.Simulate(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow((TileKind)kind, stage: stage),
            RunBattleWorlds.Content);

        result.Log.ShouldNotBeEmpty("a composed fight that ran no ticks composed no roster");
        result.DurationTicks.ShouldBeGreaterThan(0);
        result.LogHash.ShouldNotBe(0UL);
    }

    /// <summary>The same run composes the same fight every time — the whole point of a seed.</summary>
    [Fact]
    public void The_same_run_composes_the_same_fight_twice()
    {
        var player = RunBattleWorlds.PlayerRow();
        var run = RunBattleWorlds.RunRow();

        RunBattle.Simulate(player, run, RunBattleWorlds.Content).LogHash.ShouldBe(
            RunBattle.Simulate(player, run, RunBattleWorlds.Content).LogHash);
    }

    /// <summary>The snapshot door and the aggregate door compose the same fight.</summary>
    /// <remarks>
    /// The confirming handler holds aggregates and the client holds rows; if the two composed
    /// different fights, every honest client would be refused.
    /// </remarks>
    [Fact]
    public void Both_doors_compose_the_same_fight()
    {
        var playerRow = RunBattleWorlds.PlayerRow();
        var runRow = RunBattleWorlds.RunRow();

        RunBattle.Simulate(RunBattleWorlds.Player(playerRow), RunBattleWorlds.Run(runRow), RunBattleWorlds.Content)
            .LogHash
            .ShouldBe(RunBattle.Simulate(playerRow, runRow, RunBattleWorlds.Content).LogHash);
    }

    // ══════════════════════════════════════════════════ the loadout reaches the fight

    /// <summary>A hero in the full set fights a different fight from a hero in nothing.</summary>
    /// <remarks>
    /// A hash difference rather than "won faster": a fight the gear did not reach is byte-identical,
    /// not slower. The two arms cross the player's CURRENT loadout against the run's FROZEN one —
    /// flipped together, the direction would be satisfied by a composition that reads the player's
    /// own loadout and never looks at the run at all.
    /// </remarks>
    [Fact]
    public void The_frozen_loadout_reaches_the_fight()
    {
        var geared = RunBattle.Simulate(
            RunBattleWorlds.PlayerRow(geared: false),
            RunBattleWorlds.RunRow(),
            RunBattleWorlds.Content);

        var bare = RunBattle.Simulate(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow(geared: false),
            RunBattleWorlds.Content);

        geared.LogHash.ShouldNotBe(bare.LogHash, "a full SS set that changes no byte of the fight is inert gear");
        geared.HeroHpRemaining.ShouldBeGreaterThan(
            bare.HeroHpRemaining,
            "and the direction is the point: the hero whose RUN froze the set is the one who takes " +
            "less of a beating, however the player is dressed now");
    }

    /// <summary>The loadout's STANDING modifiers reach the fight, not only its triggered ones.</summary>
    /// <remarks>
    /// The case above cannot see this: gear stats are synthesised with an explicit <c>ALWAYS</c>
    /// trigger, and a collector reading only the ABSENT spelling drops the whole standing half while
    /// the set's <c>ON_KILL</c> bonus still makes a geared fight differ from a bare one. So the
    /// comparison is the composed fight against the same fight with only the standing half struck out.
    /// </remarks>
    [Fact]
    public void The_loadouts_standing_modifiers_reach_the_fight_and_not_only_its_triggered_ones()
    {
        var runRow = RunBattleWorlds.RunRow();
        var build = HeroBuild.Of(RunBattleWorlds.Player(), RunBattleWorlds.Run(runRow), RunBattleWorlds.Content);
        var standing = RunBattleTestArithmetic.StandingEffects(build);

        standing.ShouldNotBeEmpty(
            "the floor: with no standing modifier in the loadout at all, striking them out changes " +
            "nothing and the assertion below would hold for free");

        standing.Select(e => e.Trigger?.Kind).ShouldContain(
            Core.Content.Effects.TriggerKind.ALWAYS,
            "and at least one has to carry an EXPLICIT ALWAYS, which is the spelling every gear, " +
            "affix and set effect uses and the one a reader of the absent form alone would drop");

        var withoutTheStandingHalf = EncounterFight.Run(
            RunBattle.SeedOf(runRow),
            build.BaseStats,
            RunBattleWorlds.LegendLevel,
            runRow.ChapterId,
            RunBattleTestArithmetic.TierOrdinal(runRow.Tier),
            new[] { RunBattleTestArithmetic.EnemyPower(runRow, RunBattleWorlds.Content) },
            eliteIndex: -1,
            RunBattleWorlds.Content,
            RunBattleTestArithmetic.HoldingsWithoutStandingModifiers(build));

        RunBattle.Simulate(RunBattleWorlds.PlayerRow(), runRow, RunBattleWorlds.Content).LogHash.ShouldNotBe(
            withoutTheStandingHalf.LogHash,
            "a fight that is byte-identical with the loadout's standing modifiers removed is a fight " +
            "none of them reached — which is gear that aggregates on a screen and does nothing at all");
    }

    /// <summary>
    /// The fight is composed from the base curve plus the effects, never from the aggregated block.
    /// </summary>
    /// <remarks>
    /// <c>BattleSimulation</c> re-aggregates the actor's effects every pass, so a pre-aggregated
    /// block handed in as <c>BaseStats</c> applies the entire loadout twice — it throws nothing, and
    /// the only symptom is a hero silently far too strong. Both arms are needed: equality with the
    /// base-curve call alone would still be green if the two calls agreed because the loadout was
    /// empty, and inequality with the pre-aggregated one is what catches the wrong block.
    /// </remarks>
    [Fact]
    public void The_fight_is_composed_from_the_base_curve_and_the_effects_not_the_aggregated_block()
    {
        var runRow = RunBattleWorlds.RunRow();
        var build = HeroBuild.Of(RunBattleWorlds.Player(), RunBattleWorlds.Run(runRow), RunBattleWorlds.Content);
        var seed = RunBattle.SeedOf(runRow);
        var powers = new[] { RunBattleTestArithmetic.EnemyPower(runRow, RunBattleWorlds.Content) };

        var holdings = RunBattleTestArithmetic.Holdings(build);
        var composed = RunBattle.Simulate(RunBattleWorlds.PlayerRow(), runRow, RunBattleWorlds.Content);

        // Both arms take the run's current HP, exactly as RunBattle.Simulate does. The subject is
        // WHICH stat block the seam hands over, so every other argument has to match the seam's, or
        // the case would compare two differences at once and report whichever it hit first.
        var fromTheCurve = EncounterFight.Run(
            seed,
            build.BaseStats,
            RunBattleWorlds.LegendLevel,
            runRow.ChapterId,
            RunBattleTestArithmetic.TierOrdinal(runRow.Tier),
            powers,
            eliteIndex: -1,
            RunBattleWorlds.Content,
            holdings,
            runRow.CurrentHp);

        var fromTheAggregate = EncounterFight.Run(
            seed,
            build.Stats,
            RunBattleWorlds.LegendLevel,
            runRow.ChapterId,
            RunBattleTestArithmetic.TierOrdinal(runRow.Tier),
            powers,
            eliteIndex: -1,
            RunBattleWorlds.Content,
            holdings,
            runRow.CurrentHp);

        composed.LogHash.ShouldBe(
            fromTheCurve.LogHash,
            "the seam has to hand the fight the base curve and the effect list, which is what the " +
            "simulator re-aggregates every pass");

        composed.LogHash.ShouldNotBe(
            fromTheAggregate.LogHash,
            "handing the simulator the ALREADY-aggregated block applies the whole loadout twice — it " +
            "throws nothing and the fight still completes, so a hash difference is the only symptom");
    }

    /// <summary>
    /// A BOSS tile is composed from the base curve and the effects too, and never from the
    /// aggregated block.
    /// </summary>
    /// <remarks>
    /// The boss branch builds its own roster through <c>BossFight</c>, so it can repeat the same
    /// silent double application one layer across — the case above cannot see it.
    /// </remarks>
    [Fact]
    public void The_boss_fight_is_composed_from_the_base_curve_and_the_effects_not_the_aggregated_block()
    {
        var runRow = RunBattleWorlds.RunRow(TileKind.Boss, stage: BoardGraph.BossStage, linearIndex: 42);
        var build = HeroBuild.Of(RunBattleWorlds.Player(), RunBattleWorlds.Run(runRow), RunBattleWorlds.Content);

        var composed = RunBattle.Simulate(RunBattleWorlds.PlayerRow(), runRow, RunBattleWorlds.Content);

        composed.LogHash.ShouldBe(
            Boss(build, runRow, build.BaseStats).LogHash,
            "the boss branch has to hand the fight the base curve and the effect list, exactly as the " +
            "encounter branch does");

        composed.LogHash.ShouldNotBe(
            Boss(build, runRow, build.Stats).LogHash,
            "handing the boss roster the ALREADY-aggregated block applies the whole loadout twice — " +
            "it throws nothing and the boss still dies, so a hash difference is the only symptom");
    }

    /// <summary>The run's own current HP reaches the simulation, not the build's full ceiling.</summary>
    /// <remarks>
    /// Not a <c>LogHash</c> comparison — the hashes are EQUAL, correctly: the hash is over the event
    /// list, and a hero that survives either way takes the same blows and simply ends lower by
    /// exactly its deficit. That deficit is the reading; while it is zero, everything that heals or
    /// hurts a run between battles is inert, `02` §6's revive included.
    /// </remarks>
    [Fact]
    public void A_wounded_run_ends_its_fight_exactly_its_deficit_lower()
    {
        // The composed base ceiling, so "full" is the hero's own number rather than the fixture row's
        // default of 100 — against which an over-par hero's health is a rounding error either way.
        var full = (int)Math.Round(
            HeroBuild.Of(
                RunBattleWorlds.Player(),
                null,
                RunBattleWorlds.Content).BaseStats[Core.Content.Effects.StatId.MAX_HP]);

        var deficit = full / 4;

        var healthy = RunBattleWorlds.RunRow() with { CurrentHp = full, MaxHp = full };
        var wounded = healthy with { CurrentHp = full - deficit };

        var player = RunBattleWorlds.PlayerRow();

        var atFull = RunBattle.Simulate(player, healthy, RunBattleWorlds.Content);
        var hurt = RunBattle.Simulate(player, wounded, RunBattleWorlds.Content);

        (atFull.HeroHpRemaining - hurt.HeroHpRemaining).ShouldBe(
            deficit,
            tolerance: 0.5,
            "a difference of zero means the run's hit points never reached the simulation at all");
    }

    /// <summary>One boss fight composed directly, at the row's own seed and power.</summary>
    private static SimulationResult Boss(
        HeroBuild build, Core.Model.Snapshots.RunSnapshot row, ActorStats hero)
    {
        var player = RunBattleWorlds.Player();

        return Core.Rules.Combat.Bosses.BossFight.Run(
            RunBattle.SeedOf(row),
            hero,
            RunBattleWorlds.LegendLevel,
            ChapterBoardTuning.Read(RunBattleWorlds.Content, row.ChapterId).BossId,
            RunBattleTestArithmetic.EnemyPower(row, RunBattleWorlds.Content),
            EnemyCatalogue.Read(RunBattleWorlds.Content)
                .Levels.Of(row.ChapterId, RunBattleTestArithmetic.TierOrdinal(row.Tier)),
            RunBattleWorlds.Content,
            !player.HasClearedChapterTier(row.ChapterId, row.Tier),
            RunBattleTestArithmetic.Holdings(build),

            // The run's own health, exactly as RunBattle.Simulate hands it over — a helper that let
            // this default to full would compose a different fight from the seam it asserts against.
            row.CurrentHp);
    }

    /// <summary>The aggregated block is the bigger block, so re-applying it can only inflate the hero.</summary>
    /// <remarks>
    /// The negative control for the pairs above: two different hashes could in principle be two
    /// equally-valid fights. This pins the direction — a block that already carries the loadout,
    /// handed back as a base curve, is strictly more hero than the curve it was built from — so those
    /// cases discriminate between a correct fight and a specific wrong one rather than between two
    /// arbitrary ones.
    /// </remarks>
    [Fact]
    public void The_aggregated_block_is_a_bigger_block_than_the_base_curve_it_was_built_from()
    {
        var runRow = RunBattleWorlds.RunRow();
        var build = HeroBuild.Of(RunBattleWorlds.Player(), RunBattleWorlds.Run(runRow), RunBattleWorlds.Content);

        RunBattleTestArithmetic.Sum(build.Stats).ShouldBeGreaterThan(
            RunBattleTestArithmetic.Sum(build.BaseStats),
            "an aggregated block is the base curve plus a whole SS set, so passing it as the base " +
            "and re-applying the set on top of it can only inflate the hero");
    }

    /// <summary>
    /// The loadout's effects reach the fight carrying the holdings they were collected under, not
    /// as bare definitions.
    /// </summary>
    /// <remarks>
    /// A roster refuses an <c>ON_KILL</c> effect arriving with no instance id — the counter is
    /// run-scoped — and the shipped BALANCED set's four-piece bonus is exactly such an effect. The
    /// two arms are the same fight differing only in whether the collected ids survived; a build
    /// with fewer than four set pieces would compose perfectly well either way.
    /// </remarks>
    [Fact]
    public void The_loadouts_effects_reach_the_fight_with_the_holdings_they_were_collected_under()
    {
        var runRow = RunBattleWorlds.RunRow();
        var build = HeroBuild.Of(RunBattleWorlds.Player(), RunBattleWorlds.Run(runRow), RunBattleWorlds.Content);

        build.Effects.Select(e => e.Trigger?.Kind).ShouldContain(
            Core.Content.Effects.TriggerKind.ON_KILL,
            "the fixture wears the full BALANCED set, whose four-piece bonus is the ON_KILL effect " +
            "this case is about — without it both arms would pass for the wrong reason");

        Should.NotThrow(
            () => RunBattle.Simulate(RunBattleWorlds.PlayerRow(), runRow, RunBattleWorlds.Content),
            "the seam carries the collected instance ids, so the roster accepts the set bonus");

        var refused = Should.Throw<ArgumentException>(
            () => CombatSimulator.SimulateEncounter(
                RunBattle.SeedOf(runRow),
                build.BaseStats,
                RunBattleWorlds.LegendLevel,
                runRow.ChapterId,
                RunBattleTestArithmetic.TierOrdinal(runRow.Tier),
                new[] { RunBattleTestArithmetic.EnemyPower(runRow, RunBattleWorlds.Content) },
                RunBattleWorlds.Content,
                heroEffects: build.Effects),
            "the public door mints battle-local ids, which is the right answer for a caller with no " +
            "run — and the proof that the ids are what makes the seam's fight composable");

        refused.Message.ShouldContain(
            "SET_BONUS_BALANCED_4",
            Case.Sensitive,
            "the refusal names the effect whose counter would have been reset");
    }

    // ══════════════════════════════════════════════════════════════ the tile's own scaling

    /// <summary>The tile's linear index reaches the enemy the seam composes.</summary>
    /// <remarks>
    /// Stated as identity, not as "the later tile hurts more": the geared fixture hero wins both
    /// fights at full health, so an outcome comparison is satisfied by a composition that ignores
    /// the index entirely — the exact defect this case is about.
    /// </remarks>
    [Fact]
    public void The_tiles_linear_index_reaches_the_enemy_the_seam_composes()
    {
        var early = RunBattleWorlds.RunRow(linearIndex: 0);
        var late = RunBattleWorlds.RunRow(linearIndex: 40);
        var build = HeroBuild.Of(
            RunBattleWorlds.Player(), RunBattleWorlds.Run(early), RunBattleWorlds.Content);

        RunBattleTestArithmetic.EnemyPower(late, RunBattleWorlds.Content).ShouldBeGreaterThan(
            RunBattleTestArithmetic.EnemyPower(early, RunBattleWorlds.Content),
            "the control: EnemyPower(i) grows with the index, or the two arms below are one arm");

        RunBattle.Simulate(RunBattleWorlds.PlayerRow(), early, RunBattleWorlds.Content)
            .LogHash.ShouldBe(Encounter(build, early, eliteIndex: -1).LogHash);

        RunBattle.Simulate(RunBattleWorlds.PlayerRow(), late, RunBattleWorlds.Content)
            .LogHash.ShouldBe(
                Encounter(build, late, eliteIndex: -1).LogHash,
                "a seam that dropped the index would fight node 40 at node 0's power");
    }

    /// <summary>A Heroic run is a harder fight than a Normal one on the same tile.</summary>
    /// <remarks>
    /// ⚠️ Read at chapter 1's LAST node, not its default seventh. Chapter 1's par is authored at
    /// 175 for a hero with NO gear at all, so at the first stage's node 7 the fixture hero one-shots
    /// either tier's draw and takes no damage at all — and the Heroic arm then ends HIGHER, on the
    /// extra tick of lifesteal its bigger enemy bought. The claim needs a node where both arms cost
    /// the hero something.
    /// </remarks>
    [Fact]
    public void A_higher_tier_is_a_harder_fight_on_the_same_tile()
    {
        var normal = RunBattle.Simulate(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow(stage: 3, linearIndex: 41, tier: DifficultyTier.NORMAL),
            RunBattleWorlds.Content);

        var heroic = RunBattle.Simulate(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow(stage: 3, linearIndex: 41, tier: DifficultyTier.HEROIC),
            RunBattleWorlds.Content);

        heroic.HeroHpRemaining.ShouldBeLessThan(
            normal.HeroHpRemaining,
            "TierMult is x4 at Heroic, so the same node is a four-times-stronger enemy");
    }

    /// <summary>An Elite tile is composed as an Elite, and an Enemy tile is not.</summary>
    /// <remarks>
    /// Identity, not difficulty: the two kinds draw from different pools, so an Elite at 2.2x power
    /// can measurably leave the hero HEALTHIER than an ordinary draw — "hits harder" cannot be made
    /// true. What is exactly true is which slot the encounter elevates, with the two arms proven
    /// distinguishable first so the pair cannot both be satisfied by one fight.
    /// </remarks>
    [Fact]
    public void An_elite_tile_is_composed_as_an_elite_and_an_enemy_tile_is_not()
    {
        var eliteRow = RunBattleWorlds.RunRow(TileKind.Elite);
        var enemyRow = RunBattleWorlds.RunRow(TileKind.Enemy);
        var build = HeroBuild.Of(
            RunBattleWorlds.Player(), RunBattleWorlds.Run(eliteRow), RunBattleWorlds.Content);

        var asElite = Encounter(build, eliteRow, eliteIndex: 0);
        var asOrdinary = Encounter(build, enemyRow, eliteIndex: -1);

        asElite.LogHash.ShouldNotBe(
            asOrdinary.LogHash,
            "the control: elevating the slot has to change the fight, or the two assertions below are " +
            "one assertion written twice");

        RunBattle.Simulate(RunBattleWorlds.PlayerRow(), eliteRow, RunBattleWorlds.Content)
            .LogHash.ShouldBe(asElite.LogHash, "an Elite tile elevates its one slot");

        RunBattle.Simulate(RunBattleWorlds.PlayerRow(), enemyRow, RunBattleWorlds.Content)
            .LogHash.ShouldBe(asOrdinary.LogHash, "an Enemy tile elevates none");
    }

    /// <summary>One encounter composed directly, at the row's own seed and power.</summary>
    private static SimulationResult Encounter(
        HeroBuild build, Core.Model.Snapshots.RunSnapshot row, int eliteIndex) =>
        EncounterFight.Run(
            RunBattle.SeedOf(row),
            build.BaseStats,
            RunBattleWorlds.LegendLevel,
            row.ChapterId,
            RunBattleTestArithmetic.TierOrdinal(row.Tier),
            new[] { RunBattleTestArithmetic.EnemyPower(row, RunBattleWorlds.Content) },
            eliteIndex,
            RunBattleWorlds.Content,
            RunBattleTestArithmetic.Holdings(build),

            // 🔒 See Boss above: the seam passes the run's current HP, so a helper comparing against it
            // has to as well.
            row.CurrentHp);

    /// <summary>A Boss tile fights the chapter's authored boss, not a nameless enemy.</summary>
    /// <remarks>
    /// Pinned through the boss's own phase mechanic rather than through "it was harder": a boss
    /// composed as an ordinary encounter would also be harder (the boss stage multiplier is 2.2), so
    /// difficulty alone cannot tell the two compositions apart. A phase transition can — nothing but
    /// <c>BossPhaseController</c> emits one.
    /// </remarks>
    [Fact]
    public void A_boss_tile_fights_the_chapters_authored_boss()
    {
        var result = RunBattle.Simulate(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow(TileKind.Boss, stage: BoardGraph.BossStage, linearIndex: 42),
            RunBattleWorlds.Content);

        result.Log.ShouldContain(
            e => e.Type == CombatEventType.PhaseChange,
            "only a real boss roster has phases — an ordinary encounter composed at boss power has none");
    }

    // ══════════════════════════════════════════════════════════════════ refusals

    /// <summary>A run that is not standing in a battle has no fight to compose.</summary>
    [Fact]
    public void A_run_that_is_not_in_a_battle_is_refused_by_phase()
    {
        var refused = Should.Throw<InvalidOperationException>(
            () => RunBattle.Simulate(
                RunBattleWorlds.PlayerRow(),
                RunBattleWorlds.RunRow(phase: RunPhase.InProgress),
                RunBattleWorlds.Content));

        refused.Message.ShouldContain(
            nameof(RunPhase.BattlePending),
            Case.Sensitive,
            "the refusal names the phase, so it cannot be confused with the pending-tile one");
    }

    /// <summary>A run in the battle phase but standing on no tile is a defect, and says so.</summary>
    [Fact]
    public void A_run_in_the_battle_phase_with_no_pending_tile_is_refused_by_the_tile()
    {
        var refused = Should.Throw<InvalidOperationException>(
            () => RunBattle.Simulate(
                RunBattleWorlds.PlayerRow(), RunBattleWorlds.OnNoTileRow(), RunBattleWorlds.Content));

        // Not "pending tile": the KIND refusal opens with "This run's pending tile is …", so the
        // shorter phrase would be satisfied by either of the two rules.
        refused.Message.ShouldContain(
            "carries no pending tile",
            Case.Insensitive,
            "a different rule from the phase gate and from the kind gate, so it has to be a " +
            "different sentence from both");
    }

    /// <summary>A pending tile of a non-fight kind cannot compose a fight.</summary>
    [Theory]
    [InlineData((int)TileKind.Shop)]
    [InlineData((int)TileKind.Treasure)]
    [InlineData((int)TileKind.Campfire)]
    public void A_pending_tile_of_a_non_fight_kind_is_refused_by_the_kind(int kind)
    {
        var refused = Should.Throw<InvalidOperationException>(
            () => RunBattle.Simulate(
                RunBattleWorlds.PlayerRow(),
                RunBattleWorlds.RunRow((TileKind)kind),
                RunBattleWorlds.Content));

        refused.Message.ShouldContain(
            ((TileKind)kind).ToString(),
            Case.Sensitive,
            "the refusal names the kind it was handed, so a reader knows which tile it was");
    }

    /// <summary>A row that does not rehydrate is refused at the door, not composed from halves.</summary>
    /// <remarks>
    /// The parameter name is asserted, not just the exception type: <c>ArgumentNullException</c> is
    /// an <c>ArgumentException</c> and so is the player door's own refusal, so a bare type check
    /// would be satisfied by a fault about the wrong argument entirely.
    /// </remarks>
    [Fact]
    public void A_run_row_that_does_not_rehydrate_is_refused_at_the_public_door()
    {
        var broken = RunBattleWorlds.RunRow() with { CurrentHp = -1 };

        var refused = Should.Throw<ArgumentException>(
            () => RunBattle.Simulate(RunBattleWorlds.PlayerRow(), broken, RunBattleWorlds.Content));

        refused.ParamName.ShouldBe("run");
        refused.Message.ShouldContain("does not rehydrate", Case.Insensitive);
    }

    // ═══════════════════════════════════════════════════════ "is there a battle open at all"

    /// <summary>Every row shape the predicate is stated over, and whether it names an open battle.</summary>
    /// <remarks>
    /// A caller above this assembly has to ask before it composes, and the only answer worth having
    /// is the composition's own. So the cases are the composition's refusals, one for one.
    /// </remarks>
    public static TheoryData<string, bool> OpenBattleRows => new()
    {
        { "enemy", true },
        { "elite", true },
        { "boss", true },
        { "not in the battle phase", false },
        { "no pending tile", false },
        { "a tile that is not a fight", false },
        { "an undrawn combat stream", false },
    };

    /// <summary>The row each case above names.</summary>
    private static RunSnapshot RowFor(string shape) => shape switch
    {
        "enemy" => RunBattleWorlds.RunRow(),
        "elite" => RunBattleWorlds.RunRow(TileKind.Elite),
        "boss" => RunBattleWorlds.RunRow(TileKind.Boss, stage: BoardGraph.BossStage),
        "not in the battle phase" => RunBattleWorlds.RunRow(phase: RunPhase.InProgress),
        "no pending tile" => RunBattleWorlds.OnNoTileRow(),
        "a tile that is not a fight" => RunBattleWorlds.RunRow(TileKind.Shop),
        "an undrawn combat stream" => RunBattleWorlds.RunRow(battlesStarted: 0UL),
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "no row is authored for it"),
    };

    /// <summary>The predicate answers each row shape the way the composition treats it.</summary>
    [Theory]
    [MemberData(nameof(OpenBattleRows))]
    public void The_predicate_names_an_open_battle_exactly_when_there_is_one(string shape, bool open) =>
        RunBattle.HasOpenBattle(RowFor(shape)).ShouldBe(
            open,
            "'" + shape + "' is " + (open ? "" : "not ") + "a run standing in a battle it can fight");

    /// <summary>
    /// The predicate and the composition agree on every one of those rows — the whole reason the
    /// predicate is here rather than in the caller.
    /// </summary>
    /// <remarks>
    /// An agreement, not a list of booleans: a predicate testing the phase alone would answer
    /// <c>true</c> for a run carrying no pending tile, and a caller would be told there is a fight
    /// and then handed an <c>InvalidOperationException</c> out of what it published as a read.
    /// </remarks>
    [Theory]
    [MemberData(nameof(OpenBattleRows))]
    public void The_predicate_agrees_with_the_composition_it_guards(string shape, bool open)
    {
        var row = RowFor(shape);

        var composes = true;

        try
        {
            RunBattle.Simulate(RunBattleWorlds.PlayerRow(), row, RunBattleWorlds.Content);
            RunBattle.SeedOf(row);
        }
        catch (InvalidOperationException)
        {
            composes = false;
        }

        composes.ShouldBe(
            open,
            "the fixture for '" + shape + "' no longer composes the way this file says it does");

        RunBattle.HasOpenBattle(row).ShouldBe(
            composes,
            "the predicate said " + RunBattle.HasOpenBattle(row) + " about '" + shape + "' and the " +
            "composition said " + composes + ". A caller asking the predicate and then composing " +
            "would be told there is a fight and then handed a fault instead of one.");
    }

    /// <summary>A row that does not rehydrate is a fault, not an absence of a battle.</summary>
    /// <remarks>
    /// The distinction is the reason the predicate rehydrates through the same door the fight comes
    /// through: answering <c>false</c> for a corrupt row would report it as a run with nothing to
    /// fight, and the corruption would never be seen by anyone.
    /// </remarks>
    [Fact]
    public void A_run_row_that_does_not_rehydrate_is_a_fault_rather_than_a_closed_battle()
    {
        var refused = Should.Throw<ArgumentException>(
            () => RunBattle.HasOpenBattle(RunBattleWorlds.RunRow() with { CurrentHp = -1 }));

        refused.ParamName.ShouldBe("run");
        refused.Message.ShouldContain("does not rehydrate", Case.Insensitive);
    }
}
