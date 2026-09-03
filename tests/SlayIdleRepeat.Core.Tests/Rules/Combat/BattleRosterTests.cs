using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// <see cref="SimulationResult.Roster"/>: one <see cref="BattleRosterEntry"/> per actor a fight ever
/// admitted, named by the door that composed it and flagged as the roster knew it.
/// </summary>
/// <remarks>
/// The encounter, elite and boss cases run the shipped chapter-1 content through the public entry
/// points. The summon case cannot: no public door admits an actor mid-fight on demand, so it drives
/// the <see cref="BossTestBench"/> roster with the real <see cref="BossSummonSource"/> the boss
/// composition wires, which is the one seam that names a summon.
/// </remarks>
public sealed class BattleRosterTests
{
    private const ulong BattleSeed = 0xB0_57_E4_0000_0002UL;
    private const double EnemyPower = 400.0;
    private const int ChapterOne = 1;
    private const int NormalTier = 0;
    private const int Level = 10;

    private const string Unnamed = "(null)";
    private const string SummonEffect = "BOSS_THORNMAW_P3_ADDS";
    private const string SummonInstance = "BOSS_THORNMAW#P3#BOSS_THORNMAW_P3_ADDS";

    private static ContentSnapshot Content => ShippedBosses.Content;

    [Fact]
    public void Roster_lists_every_ActorSpawned_target_once_in_spawn_order()
    {
        var run = SummonFight();

        var spawned = run.Result.Log
            .Where(e => e.Type == CombatEventType.ActorSpawned)
            .Select(e => e.TargetId)
            .Distinct()
            .ToArray();

        spawned.Length.ShouldBeGreaterThanOrEqualTo(
            4,
            "the floor: the hero, the boss and the two adds admitted at tick 60 — without a mid-fight " +
            "spawn the order claim below would hold for a roster snapshotted at the pre-tick");

        run.Result.Roster.Select(r => r.ActorId).ShouldBe(
            spawned, "one entry per actor ever admitted, summons included, in index order");
    }

    [Fact]
    public void The_hero_is_the_first_roster_entry_and_is_named_HERO()
    {
        var result = Encounter([EnemyPower]);

        result.Roster.Count.ShouldBeGreaterThanOrEqualTo(2, "the floor: the hero and one enemy");
        result.Roster[0].ActorId.ShouldBe(CombatActor.Hero);
        result.Roster[0].Identity.ShouldBe("HERO");
    }

    [Fact]
    public void An_enemy_tile_names_each_slot_by_the_drawn_archetype_whose_statline_it_spawned_with()
    {
        var catalogue = EnemyCatalogue.Read(Content);
        var pool = catalogue.Pool(ChapterOne);
        var drawable = pool.Weights
            .Where(w => w.Weight > 0.0)
            .Select(w => w.Archetype.ToString())
            .ToArray();

        drawable.Length.ShouldBeGreaterThanOrEqualTo(1, "the floor: something to draw");
        drawable.Length.ShouldBeLessThan(
            pool.Weights.Count,
            "the floor that makes 'weighted' mean anything: chapter 1 authors at least one archetype " +
            "at weight 0, so an identity read off the wrong table could name it");

        var result = Encounter([EnemyPower, EnemyPower, EnemyPower]);

        var spawnedMaxHp = result.Log
            .Where(e => e.Type == CombatEventType.ActorSpawned && e.TargetId >= CombatActor.FirstEnemy)
            .ToDictionary(e => e.TargetId, e => e.Value);
        var enemies = result.Roster.Where(r => r.ActorId >= CombatActor.FirstEnemy).ToArray();

        enemies.Length.ShouldBe(3, "one entry per enemy power handed in");
        enemies.Select(e => e.Identity ?? Unnamed).ShouldBeSubsetOf(
            drawable, "the archetype the pool drew, by its exact enum name");
        enemies.Select(e => e.Identity).Distinct().Count().ShouldBeGreaterThanOrEqualTo(
            2,
            "the floor that makes the statline check discriminating: this seed draws two archetypes, " +
            "so one name written on every slot cannot match every body");
        enemies.Select(e => MaxHpOf(catalogue, e.Identity)).ShouldBe(
            enemies.Select(e => spawnedMaxHp[e.ActorId]),
            "each slot's name is the archetype whose statline the log spawned that slot with — the " +
            "body the name dresses is the body the server fought, not the pool's first row");
        enemies.ShouldAllBe(e => !e.IsElite && !e.IsBoss && !e.IsSummon);
    }

    [Fact]
    public void An_elite_slot_names_one_of_the_chapters_elite_pool_and_is_flagged_elite()
    {
        var pool = EnemyCatalogue.Read(Content).Pool(ChapterOne);
        var elitePool = pool.ElitePool.ToArray();

        elitePool.Length.ShouldBeGreaterThanOrEqualTo(1, "the floor: an elite to draw");

        var result = Encounter([EnemyPower, EnemyPower], eliteIndex: 1);

        result.Roster.Select(r => r.ActorId).ShouldBe(
            [CombatActor.Hero, CombatActor.Enemy(0), CombatActor.Enemy(1)],
            "the floor: both slots are listed, the elite one included");

        var elite = result.Roster.Single(r => r.ActorId == CombatActor.Enemy(1));
        var plain = result.Roster.Single(r => r.ActorId == CombatActor.Enemy(0));

        elite.IsElite.ShouldBeTrue();
        elite.IsBoss.ShouldBeFalse();
        elite.IsSummon.ShouldBeFalse();
        (elite.Identity ?? Unnamed).ShouldBeOneOf(elitePool);
        (elite.Identity ?? Unnamed).ShouldNotBeOneOf(
            Enum.GetNames<EnemyArchetype>(),
            "the elite id the pool drew, not the base archetype its statline derives from");

        plain.IsElite.ShouldBeFalse("the other slot is an ordinary tile");
        (plain.Identity ?? Unnamed).ShouldNotBeOneOf(elitePool, "and it is not named as one");
    }

    [Fact]
    public void A_boss_fight_names_the_boss_by_its_script_id_and_flags_it_boss()
    {
        var result = BossFight();

        result.Roster.ShouldContain(
            r => r.ActorId == CombatActor.Enemy(0), "the floor: the boss holds enemy slot 0");

        var boss = result.Roster.Single(r => r.ActorId == CombatActor.Enemy(0));

        boss.Identity.ShouldBe(BossTestBench.Thornmaw);
        boss.IsBoss.ShouldBeTrue();
        boss.IsElite.ShouldBeFalse();
        boss.IsSummon.ShouldBeFalse();
    }

    [Fact]
    public void A_boss_fight_names_its_hero_through_its_own_door()
    {
        var result = BossFight();

        result.Roster.Count.ShouldBeGreaterThanOrEqualTo(2, "the floor: the hero and the boss");
        result.Roster[0].ActorId.ShouldBe(CombatActor.Hero);
        result.Roster[0].Identity.ShouldBe(
            "HERO", "BossFight composes its own hero plan, not EncounterFight's");
    }

    [Fact]
    public void A_summon_admitted_mid_fight_is_flagged_summon_and_named_by_its_archetype()
    {
        var run = SummonFight();

        var adds = run.Result.Roster
            .Where(r => r.ActorId != CombatActor.Hero && r.ActorId != CombatActor.Enemy(0))
            .ToArray();

        adds.Length.ShouldBe(2, "the floor: the bench's SUMMON op authors two adds on the phase-3 entry");
        adds.Select(a => a.ActorId).ShouldBe(
            [CombatActor.Enemy(1), CombatActor.Enemy(2)], "the next free ids, after the boss's");
        adds.ShouldAllBe(a => a.IsSummon);
        adds.Select(a => a.Identity ?? Unnamed).ShouldBe(
            [nameof(EnemyArchetype.SWARM), nameof(EnemyArchetype.SWARM)],
            "the archetype the SUMMON op authored, as BossSummonSource spawned it");
        adds.ShouldAllBe(a => !a.IsBoss && !a.IsElite);
    }

    [Fact]
    public void A_stat_block_fight_names_nobody_but_still_lists_every_spawned_actor()
    {
        var result = CombatSimulator.Simulate(
            BattleSeed,
            StatFixtures.HeroCurve().At(60),
            heroLevel: 60,
            [BattleTestBench.Stats(maxHp: 300, atk: 8), BattleTestBench.Stats(maxHp: 200, atk: 6)],
            enemyLevel: Level,
            Content);

        result.Roster.Select(r => r.ActorId).ShouldBe(
            [CombatActor.Hero, CombatActor.Enemy(0), CombatActor.Enemy(1)],
            "the floor: every spawned actor is listed even when no door named it");
        result.Roster.ShouldAllBe(
            r => r.Identity == null,
            "the stat-block door names nobody — null, never a plausible string");
        result.Roster.ShouldAllBe(r => !r.IsElite && !r.IsBoss && !r.IsSummon);
    }

    [Fact]
    public void The_LogHash_is_computed_over_the_log_alone_with_the_roster_outside_it()
    {
        var result = Encounter([EnemyPower]);

        result.Roster.Count.ShouldBeGreaterThanOrEqualTo(2, "the floor: a roster that could have been hashed");
        result.LogHash.ShouldBe(
            CanonicalStateWriter.HashCombatLog(result.Log),
            "the roster is the cast list a renderer dresses the replay with, not part of the replay's " +
            "integrity check — a client and a server that agree on every event keep agreeing whether " +
            "or not either carries the names");
    }

    /// <summary>The Max HP the encounter door derives for an archetype at <see cref="EnemyPower"/>; NaN for a name that is no archetype.</summary>
    private static double MaxHpOf(EnemyCatalogue catalogue, string? identity) =>
        EnemyArchetypes.TryParse(identity ?? Unnamed, out var archetype)
            ? EnemyDerivation.Derive(EnemyPower, catalogue.Archetype(archetype), catalogue.Derivation)[StatId.MAX_HP]
            : double.NaN;

    private static SimulationResult Encounter(IReadOnlyList<double> enemyPowers, int eliteIndex = -1) =>
        CombatSimulator.SimulateEncounter(
            BattleSeed, RealBossFight.Hero(), Level, ChapterOne, NormalTier, enemyPowers, Content, eliteIndex);

    private static SimulationResult BossFight() =>
        CombatSimulator.SimulateBossFight(
            RealBossFight.BattleSeed,
            RealBossFight.Hero(),
            RealBossFight.Level,
            BossTestBench.Thornmaw,
            RealBossFight.BossPower,
            RealBossFight.Level,
            Content);

    /// <summary>
    /// Thornmaw's phase-3 entry summon on the bench roster, spawned by the real
    /// <see cref="BossSummonSource"/>: the HP script puts the boss at 20% on tick 60, and the two adds
    /// are admitted on that tick.
    /// </summary>
    private static BossRun SummonFight()
    {
        var boss = BossTestBench.Boss(
            BossTestBench.Thornmaw,
            maxHp: 1000.0,
            BossTestBench.InPhase(BossTestBench.Thornmaw, 3, BossTestBench.Summon(SummonEffect)));

        var encounter = new BossEncounter
        {
            BossId = BossTestBench.Thornmaw,
            Plan = boss,
            FirstClear = false,
            Phase2HpFraction = 0.66,
            Phase3HpFraction = 0.33,
            PhaseOfInstance = new Dictionary<EffectInstanceId, int>
            {
                [EffectInstanceId.Of(SummonInstance)] = 3,
            },
            LeadSecondsOfInstance = new Dictionary<EffectInstanceId, double>(),
            AnnouncingOfPhase = new Dictionary<int, IReadOnlyList<EffectInstanceId>>(),
        };

        return BossTestBench.Run(
            [BossTestBench.Hero(), boss],
            [encounter],
            [EffectInstanceId.Of(SummonInstance)],
            [(60, BossTestBench.Thornmaw, 0.20)],
            summons: new BossSummonSource(
                BossTestBench.Catalogue(), bossPower: 10_000.0, powerFraction: 0.30, level: Level));
    }
}
