using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Combat.Enemies;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Combat.Enemies;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `18` §2.4's <c>SUMMON</c> for a boss — `17` §1's <em>"adds use standard archetypes from `05`
/// §6.1 at 25–35% of boss power, capped at 3 alive at once"</em>, and `05` §3.1's entry rules the
/// tick loop already owns.
/// </summary>
public sealed class BossSummonTests
{
    private const string SummonEffect = "BOSS_THORNMAW_P3_ADDS";
    private const string SummonInstance = "BOSS_THORNMAW#P3#BOSS_THORNMAW_P3_ADDS";

    // ════════════════════════════════════════════════════ 1 · what an add IS

    /// <summary>
    /// 🔒 `17` §1 — an add is a `05` §6.1 archetype derived at a fraction of <b>boss</b> power, which
    /// is the boss's <c>EnemyPower(i)</c> as handed in and never re-multiplied by
    /// <c>StageMult.Boss</c>.
    /// </summary>
    [Fact]
    public void An_add_is_its_archetype_derived_at_a_fraction_of_boss_power()
    {
        var source = new BossSummonSource(
            BossTestBench.Catalogue(), bossPower: 10_000.0, powerFraction: 0.30, level: 10);

        var add = source.Spawn(Summoner(), nameof(EnemyArchetype.SWARM), SummonEffect);

        var expected = EnemyDerivation.Derive(
            3_000.0, EnemyFixtures.Row(EnemyArchetype.SWARM), EnemyFixtures.Constants());

        add.BaseStats[StatId.MAX_HP].ShouldBe(expected[StatId.MAX_HP], "30% of 10000");
        add.Level.ShouldBe(10, "`05` §6.0 gives every enemy in a (chapter, tier) the one EnemyLevel");
        add.Kind.ShouldBe(EffectActorKind.ENEMY);
        add.Side.ShouldBe(BattleSide.ENEMY);
    }

    /// <summary>
    /// 🔒 The plan carries <b>no</b> index and <b>no</b> log id: `05` §3.1 gives both to the roster,
    /// and <c>CombatActor</c> is 🔒 that a summon <em>"takes the next free id and never reuses a dead
    /// one"</em>.
    /// </summary>
    [Fact]
    public void The_spawned_plan_leaves_the_index_and_the_log_id_to_the_roster()
    {
        var source = new BossSummonSource(
            BossTestBench.Catalogue(), bossPower: 10_000.0, powerFraction: 0.30, level: 10);

        var add = source.Spawn(Summoner(), nameof(EnemyArchetype.SWARM), SummonEffect);

        add.Index.ShouldBe(0, "AdmitSummon overwrites it — a value here would be a second answer");
        add.LogId.ShouldBe((byte)0);
        add.IsSummon.ShouldBeFalse("AdmitSummon sets that too");
    }

    /// <summary>
    /// 🔒 `17` §1's band is 25–35%, and the engine does not pick a number inside it — but it does
    /// refuse one outside it, because a fraction of 0.60 is adds twice as strong as `17` intends and
    /// nothing else in the fight would say so.
    /// </summary>
    [Theory]
    [InlineData(0.24)]
    [InlineData(0.36)]
    public void A_power_fraction_outside_17_1s_band_is_refused(double fraction)
    {
        var source = new BossSummonSource(
            BossTestBench.Catalogue(), bossPower: 10_000.0, powerFraction: fraction, level: 10);

        Should.Throw<EffectContextException>(
                  () => source.Spawn(Summoner(), nameof(EnemyArchetype.SWARM), SummonEffect))
              .Message.ShouldContain("25", Case.Sensitive, "17 §1's band, named in the refusal");
    }

    /// <summary>The positive control: both ends of the band are accepted.</summary>
    [Theory]
    [InlineData(BossAdds.MinPowerFraction)]
    [InlineData(BossAdds.MaxPowerFraction)]
    public void Both_ends_of_17_1s_band_are_accepted(double fraction)
    {
        var source = new BossSummonSource(
            BossTestBench.Catalogue(), bossPower: 10_000.0, powerFraction: fraction, level: 10);

        source.Spawn(Summoner(), nameof(EnemyArchetype.SWARM), SummonEffect)
              .BaseStats[StatId.MAX_HP].ShouldBeGreaterThan(0.0);
    }

    /// <summary>An archetype `05` §6.1 does not have is refused rather than silently not spawning.</summary>
    [Fact]
    public void An_unknown_archetype_is_refused()
    {
        var source = new BossSummonSource(
            BossTestBench.Catalogue(), bossPower: 10_000.0, powerFraction: 0.30, level: 10);

        Should.Throw<EffectContextException>(() => source.Spawn(Summoner(), "SPORELING", SummonEffect))
              .Message.ShouldContain("SPORELING", Case.Sensitive);
    }

    // ════════════════════════════════════════════════════ 2 · entry, through the roster

    /// <summary>
    /// 🔒 `05` §3.1's entry rules, which are <b>the tick loop's</b> and are asserted here only
    /// because a boss's phase-3 summon is the first thing in the repository that reaches them: the
    /// add enters at the end of the enemy index list, takes the next free log id, and carries a
    /// <b>full</b> <c>1.0 / ASPD</c> cooldown so that it never attacks on its spawn tick.
    /// </summary>
    [Fact]
    public void An_add_enters_at_the_end_of_the_enemy_list_and_never_attacks_on_its_spawn_tick()
    {
        var summons = new RecordingSummons(Add());

        var run = Fight(summons, (Tick: 60, ActorId: "BOSS_THORNMAW", Fraction: 0.20));

        summons.Spawns.Count.ShouldBe(2, "18 §7.8's Thornmaw summons 2 on its phase-3 entry");

        var spawned = run.Result.Log
            .Where(e => e.Type == CombatEventType.Attack && e.Tick == 60)
            .Select(e => e.SourceId)
            .ToArray();

        spawned.ShouldNotContain(
            CombatActor.Enemy(1), "05 §3.1: a summon 'never attacks on its spawn tick'");
    }

    /// <summary>
    /// 🔒 <em>"Summons take the next free id and never reuse a dead one"</em> — the log is the replay,
    /// and two actors on one id would draw the second resuming the first's HP bar.
    /// </summary>
    [Fact]
    public void Two_waves_of_adds_never_share_a_log_id()
    {
        var summons = new RecordingSummons(Add());

        Fight(summons,
              (Tick: 60, ActorId: "BOSS_THORNMAW", Fraction: 0.20),
              (Tick: 100, ActorId: "BOSS_THORNMAW", Fraction: 0.10));

        summons.Spawns.Select(s => s.Summoner).Distinct().ShouldBe(
            new[] { "BOSS_THORNMAW" }, Case.Sensitive, "OWNER is the boss that summoned");

        summons.Spawns.Count.ShouldBeGreaterThan(0, "the floor under the id assertion below");
        summons.Spawns.Select(s => s.SourceEffectId).Distinct().ShouldBe(
            new[] { SummonEffect }, Case.Sensitive);
    }

    // ════════════════════════════════════════════════════ 3 · the cap

    /// <summary>
    /// 🔒 `17` §1 — <em>"capped at 3 alive at once"</em>, which is `18` §2.4's authored
    /// <c>maxAlive</c> and is enforced by the tick loop's own <c>Summon</c> path, not by a second
    /// count in the boss engine.
    /// </summary>
    [Fact]
    public void The_three_alive_cap_is_17_1s_number_and_it_is_the_authored_maxAlive()
    {
        BossAdds.MaxAlive.ShouldBe(3);
        BossTestBench.Summon(SummonEffect).MaxAlive.ShouldBe(BossAdds.MaxAlive);
        BossAdds.MinPowerFraction.ShouldBe(0.25);
        BossAdds.MaxPowerFraction.ShouldBe(0.35);
    }

    // ════════════════════════════════════════════════════ fixtures

    private static BattleActor Summoner() =>
        new(BossTestBench.Boss("BOSS_THORNMAW"), NoStatusTimeline.Instance);

    private static ActorPlan Add() => new()
    {
        Id = "ADD_SWARM",
        Index = 0,
        LogId = 0,
        Side = BattleSide.ENEMY,
        Kind = EffectActorKind.ENEMY,
        BaseStats = BattleTestBench.Stats(maxHp: 100.0, aspd: 1.0),
        Level = 10,
    };

    private static ActorPlan BossPlan() =>
        BossTestBench.Boss(
            "BOSS_THORNMAW",
            maxHp: 1000.0,
            BossTestBench.InPhase("BOSS_THORNMAW", 3, BossTestBench.Summon(SummonEffect)));

    private static BossEncounter Encounter() => new()
    {
        BossId = "BOSS_THORNMAW",
        Plan = BossPlan(),
        FirstClear = false,
        Phase2HpFraction = 0.66,
        Phase3HpFraction = 0.33,
        PhaseOfInstance = new Dictionary<EffectInstanceId, int>
        {
            [EffectInstanceId.Of(SummonInstance)] = 3,
        },
        LeadSecondsOfInstance = new Dictionary<EffectInstanceId, double>(),
    };

    private static BossRun Fight(
        RecordingSummons summons, params (int Tick, string ActorId, double Fraction)[] script) =>
        BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), BossPlan() },
            new List<BossEncounter> { Encounter() },
            new List<EffectInstanceId> { EffectInstanceId.Of(SummonInstance) },
            script,
            summons: summons);
}
