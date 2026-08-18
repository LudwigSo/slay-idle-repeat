using Shouldly;
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
/// Before this seam a run could enter <c>BattlePending</c> and nothing anywhere could produce the
/// fight it was standing in — the loop stopped at the third verb. Every case here is stated so it
/// fails if a single link is dropped: the loadout, the aggregation input, the tile's power, the
/// tile's kind, or the battle index the seed is derived from.
/// </remarks>
public sealed class RunBattleTests
{
    // ══════════════════════════════════════════════════════════════════ the seed

    /// <summary>
    /// The battle a run is standing in is the one BEFORE its combat counter, and the seed is that
    /// index's.
    /// </summary>
    /// <remarks>
    /// 🔒 The counter counts battles STARTED, so the open battle's index is <c>counter − 1</c>. An
    /// off-by-one here is not a crash: it is a real seed for the wrong fight, which the server would
    /// then recompute differently from the client and refuse. The assertion names the derivation
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

        refused.Message.ShouldContain(
            "combat",
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
    /// 🔒 The seam M7-06c will recompute through. The confirming handler holds aggregates and the
    /// client holds rows; if the two composed different fights, every honest client would be refused
    /// and the check would be worse than absent.
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
    /// 🔒 The milestone's claim in one line: gear was carried and inert for the whole of M4, and
    /// "wearing a full SS set changes nothing about the fight" was literally true. Stated as a hash
    /// difference rather than as "won faster", because a fight the gear did not reach is not merely
    /// slower — it is byte-identical, and the hash is the one assertion that cannot be satisfied by
    /// noise.
    /// </remarks>
    [Fact]
    public void The_frozen_loadout_reaches_the_fight()
    {
        var geared = RunBattle.Simulate(
            RunBattleWorlds.PlayerRow(), RunBattleWorlds.RunRow(), RunBattleWorlds.Content);

        var bare = RunBattle.Simulate(
            RunBattleWorlds.PlayerRow(geared: false),
            RunBattleWorlds.RunRow(geared: false),
            RunBattleWorlds.Content);

        geared.LogHash.ShouldNotBe(bare.LogHash, "a full SS set that changes no byte of the fight is inert gear");
        geared.HeroHpRemaining.ShouldBeGreaterThan(
            bare.HeroHpRemaining,
            "and the direction is the point: the geared hero is the one who takes less of a beating");
    }

    /// <summary>
    /// 🔒 The fight is composed from the base curve plus the effects, and never from the aggregated
    /// block.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The hazard this case exists for.</b> <c>BattleSimulation</c> re-aggregates the actor's
    /// effects every pass, so a pre-aggregated stat block handed in as <c>BaseStats</c> applies the
    /// entire loadout <em>twice</em>. It throws nothing, the fight still completes, and the only
    /// visible symptom is a hero who is silently far too strong. Both arms are stated: the
    /// composition must equal the base-curve call, and must NOT equal the pre-aggregated one — the
    /// second is what would still be green if the seam passed the wrong block, and the first is what
    /// would still be green if the two calls happened to agree because the loadout was empty.
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

        var fromTheCurve = EncounterFight.Run(
            seed,
            build.BaseStats,
            RunBattleWorlds.LegendLevel,
            runRow.ChapterId,
            RunBattleTestArithmetic.TierOrdinal(runRow.Tier),
            powers,
            eliteIndex: -1,
            RunBattleWorlds.Content,
            holdings);

        var fromTheAggregate = EncounterFight.Run(
            seed,
            build.Stats,
            RunBattleWorlds.LegendLevel,
            runRow.ChapterId,
            RunBattleTestArithmetic.TierOrdinal(runRow.Tier),
            powers,
            eliteIndex: -1,
            RunBattleWorlds.Content,
            holdings);

        composed.LogHash.ShouldBe(
            fromTheCurve.LogHash,
            "the seam has to hand the fight the base curve and the effect list, which is what the " +
            "simulator re-aggregates every pass");

        composed.LogHash.ShouldNotBe(
            fromTheAggregate.LogHash,
            "handing the simulator the ALREADY-aggregated block applies the whole loadout twice — it " +
            "throws nothing and the fight still completes, so a hash difference is the only symptom");
    }

    /// <summary>The double-applied hero is measurably the stronger one, so the arms above are not noise.</summary>
    /// <remarks>
    /// The negative control for the pair above: two different hashes could in principle be two
    /// equally-valid fights. This pins the direction — applying a loadout twice makes the hero
    /// stronger, never weaker — so the case above is discriminating between a correct fight and a
    /// specific wrong one rather than between two arbitrary ones.
    /// </remarks>
    [Fact]
    public void Applying_the_loadout_twice_makes_the_hero_stronger_not_merely_different()
    {
        var runRow = RunBattleWorlds.RunRow();
        var build = HeroBuild.Of(RunBattleWorlds.Player(), RunBattleWorlds.Run(runRow), RunBattleWorlds.Content);

        RunBattleTestArithmetic.Sum(build.Stats).ShouldBeGreaterThan(
            RunBattleTestArithmetic.Sum(build.BaseStats),
            "an aggregated block is the base curve plus a whole SS set, so passing it as the base " +
            "and re-applying the set on top of it can only inflate the hero");
    }

    /// <summary>
    /// 🔒 The loadout's effects reach the fight carrying the holdings they were collected under, not
    /// as bare definitions.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>A hero in a full authored set cannot be put in a fight without them.</b> A roster refuses
    /// an <c>ON_KILL</c> effect that arrives with no instance id — the counter is run-scoped and a
    /// battle-local id would reset it every fight — and the four-piece bonus of the shipped BALANCED
    /// set is exactly such an effect. So the two arms below are the same fight differing only in
    /// whether the ids survived: with them it runs, without them the roster refuses by name. Nothing
    /// else in the suite would notice, because a build with fewer than four set pieces composes
    /// perfectly well either way.
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

    /// <summary>A tile further along the track is a harder fight than the same tile near the start.</summary>
    /// <remarks>
    /// The linear index is what the enemy-power curve scales on, so a composition that dropped it —
    /// or read the node id instead — would fight every tile of a chapter at identical strength. HP
    /// remaining rather than the hash, because the direction is the claim.
    /// </remarks>
    [Fact]
    public void A_later_tile_is_a_harder_fight_than_an_earlier_one()
    {
        var early = RunBattle.Simulate(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow(linearIndex: 0),
            RunBattleWorlds.Content);

        var late = RunBattle.Simulate(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow(linearIndex: 40),
            RunBattleWorlds.Content);

        late.HeroHpRemaining.ShouldBeLessThan(
            early.HeroHpRemaining,
            "EnemyPower(i) grows with the linear index, so node 40 has to hit harder than node 0");
    }

    /// <summary>A Heroic run is a harder fight than a Normal one on the same tile.</summary>
    [Fact]
    public void A_higher_tier_is_a_harder_fight_on_the_same_tile()
    {
        var normal = RunBattle.Simulate(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow(tier: DifficultyTier.NORMAL),
            RunBattleWorlds.Content);

        var heroic = RunBattle.Simulate(
            RunBattleWorlds.PlayerRow(),
            RunBattleWorlds.RunRow(tier: DifficultyTier.HEROIC),
            RunBattleWorlds.Content);

        heroic.HeroHpRemaining.ShouldBeLessThan(
            normal.HeroHpRemaining,
            "TierMult is x4 at Heroic, so the same node is a four-times-stronger enemy");
    }

    /// <summary>An Elite tile is composed as an Elite, and an Enemy tile is not.</summary>
    /// <remarks>
    /// 🔴 <b>Stated as identity, not as difficulty, and the first draft of this case had it wrong.</b>
    /// "An Elite hits harder than an ordinary enemy at the same node" is not true and cannot be made
    /// true: the two draw from different pools, so the Elite's base archetype is a different shape,
    /// and an Elite at 2.2x power measurably left the hero <em>healthier</em> than an ordinary draw
    /// did. What IS exactly true is which slot the encounter elevates — so each arm is compared
    /// against a directly composed encounter, and the two arms are proven distinguishable first, so
    /// the pair cannot both be satisfied by one fight.
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
            RunBattleTestArithmetic.Holdings(build));

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

        refused.Message.ShouldContain(
            "pending tile",
            Case.Insensitive,
            "a different rule from the phase gate, so it has to be a different sentence");
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
    [Fact]
    public void A_run_row_that_does_not_rehydrate_is_refused_at_the_public_door()
    {
        var broken = RunBattleWorlds.RunRow() with { CurrentHp = -1 };

        Should.Throw<ArgumentException>(
            () => RunBattle.Simulate(RunBattleWorlds.PlayerRow(), broken, RunBattleWorlds.Content));
    }

    /// <summary>Neither door accepts a null.</summary>
    [Fact]
    public void The_public_door_refuses_a_null_argument()
    {
        Should.Throw<ArgumentNullException>(
            () => RunBattle.Simulate(null!, RunBattleWorlds.RunRow(), RunBattleWorlds.Content));

        Should.Throw<ArgumentNullException>(
            () => RunBattle.Simulate(RunBattleWorlds.PlayerRow(), null!, RunBattleWorlds.Content));

        Should.Throw<ArgumentNullException>(
            () => RunBattle.Simulate(RunBattleWorlds.PlayerRow(), RunBattleWorlds.RunRow(), null!));
    }
}
