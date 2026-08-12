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

        // 🔴 The negative control. Without it "derived at 3000" is indistinguishable from "derived at
        //    the boss's own power and happening to agree", which is exactly the 25–35% band going
        //    unread — Thornmaw's phase-3 adds each as strong as Thornmaw.
        add.BaseStats[StatId.MAX_HP].ShouldNotBe(
            EnemyDerivation.Derive(
                10_000.0, EnemyFixtures.Row(EnemyArchetype.SWARM), EnemyFixtures.Constants())
                [StatId.MAX_HP],
            "17 §1 gives the add a FRACTION of boss power, not boss power");
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

        // 🔒 Steering S2 — WHICH rule fired. A bare `ShouldContain("25")` stood here and matched any
        //    message carrying those two digits, including the level, the power and the archetype
        //    count that every other refusal in this class also prints.
        var thrown = Should.Throw<EffectContextException>(
            () => source.Spawn(Summoner(), nameof(EnemyArchetype.SWARM), SummonEffect));

        thrown.Message.ShouldStartWith(EffectContextException.Marker, Case.Sensitive);
        thrown.Message.ShouldContain(SummonEffect, Case.Sensitive, "which mechanic");
        thrown.Message.ShouldContain("0.25", Case.Sensitive, "17 §1's floor, named in the refusal");
        thrown.Message.ShouldContain("0.35", Case.Sensitive, "and its ceiling");
        thrown.Message.ShouldContain(
            fraction.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            Case.Sensitive,
            "and the fraction actually asked for — without it the refusal cannot be told from the " +
            "unknown-archetype one");
    }

    /// <summary>
    /// The positive control: both ends of the band are accepted, and each derives the statline `17`
    /// §1's fraction of boss power gives it.
    /// </summary>
    /// <remarks>
    /// 🔴 <c>ShouldBeGreaterThan(0.0)</c> stood here and was true of every Max HP a derivation could
    /// possibly produce — including one derived at the boss's own power, which is the failure the
    /// band exists to prevent (steering S1). The two ends are pinned to
    /// <see cref="EnemyDerivation"/>'s answer at the two powers the band names, which differ from
    /// each other and from full boss power.
    /// </remarks>
    [Theory]
    [InlineData(BossAdds.MinPowerFraction, 2_500.0)]
    [InlineData(BossAdds.MaxPowerFraction, 3_500.0)]
    public void Both_ends_of_17_1s_band_are_accepted(double fraction, double expectedPower)
    {
        var source = new BossSummonSource(
            BossTestBench.Catalogue(), bossPower: 10_000.0, powerFraction: fraction, level: 10);

        var expected = EnemyDerivation.Derive(
            expectedPower, EnemyFixtures.Row(EnemyArchetype.SWARM), EnemyFixtures.Constants());

        var add = source.Spawn(Summoner(), nameof(EnemyArchetype.SWARM), SummonEffect);

        add.BaseStats[StatId.MAX_HP].ShouldBe(expected[StatId.MAX_HP]);
        add.BaseStats[StatId.ATK].ShouldBe(expected[StatId.ATK]);
    }

    /// <summary>
    /// 🔒 And the two ends do not agree: `17` §1's band is 25–<b>35</b>%, so the strong end really is
    /// stronger. Without this the two rows above would both pass on a source that ignored the
    /// fraction entirely and always derived at one number.
    /// </summary>
    [Fact]
    public void The_two_ends_of_the_band_derive_different_adds()
    {
        var weak = new BossSummonSource(
            BossTestBench.Catalogue(), 10_000.0, BossAdds.MinPowerFraction, 10)
            .Spawn(Summoner(), nameof(EnemyArchetype.SWARM), SummonEffect);

        var strong = new BossSummonSource(
            BossTestBench.Catalogue(), 10_000.0, BossAdds.MaxPowerFraction, 10)
            .Spawn(Summoner(), nameof(EnemyArchetype.SWARM), SummonEffect);

        strong.BaseStats[StatId.MAX_HP].ShouldBeGreaterThan(weak.BaseStats[StatId.MAX_HP]);
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

        // 🔒 "At the END of the enemy index list" — the half of the rule the name promises and the
        //    swing assertions below cannot see. The boss holds enemy index 0, so the two adds take
        //    1 and 2, in spawn order and after every opening enemy.
        var enemies = run.Driver.Actors
            .Where(a => a.Side == BattleSide.ENEMY)
            .OrderBy(a => a.Index)
            .ToArray();

        enemies.Length.ShouldBe(3, "the floor: the boss and its two adds");
        enemies[0].IsBoss.ShouldBeTrue("the opening roster keeps index 0");
        enemies.Skip(1).Select(a => a.Id).ShouldBe(
            new[] { "ADD_SWARM_1", "ADD_SWARM_2" },
            Case.Sensitive,
            "05 §3.1: 'summons enter at the end of the enemy index list', in spawn order");

        var addSwings = run.Result.Log
            .Where(e => e.Type == CombatEventType.Attack && e.SourceId == CombatActor.Enemy(1))
            .Select(e => e.Tick)
            .ToArray();

        // 🔴 THE FLOOR, and without it the claim below is vacuous: an add that never swung at all
        //    would satisfy "it did not swing on tick 60" perfectly, and so would an add that was
        //    never admitted to the roster. The add's ASPD is 1.0, so `05` §3.1's full 1.0/ASPD entry
        //    cooldown puts its first swing exactly 20 ticks after the spawn.
        addSwings.Length.ShouldBeGreaterThan(
            0, "the add IS on the roster and DOES swing — otherwise the assertion below proves nothing");

        addSwings.ShouldNotContain(
            60, "05 §3.1: a summon 'never attacks on its spawn tick'");

        addSwings[0].ShouldBe(80, "a FULL 1.0 / ASPD cooldown from the spawn tick, not a partial one");
    }

    /// <summary>`17` §1's <c>OWNER</c> bookkeeping: every add names the boss that summoned it.</summary>
    [Fact]
    public void Every_add_names_the_boss_that_summoned_it_and_the_mechanic_that_did()
    {
        var summons = new RecordingSummons(Add());

        Fight(summons, (Tick: 60, ActorId: "BOSS_THORNMAW", Fraction: 0.20));

        summons.Spawns.Count.ShouldBe(2, "the floor: 17 §2's Thornmaw summons 2 on its phase-3 entry");

        summons.Spawns.Select(s => s.Summoner).Distinct().ShouldBe(
            new[] { "BOSS_THORNMAW" }, Case.Sensitive, "OWNER is the boss that summoned");
        summons.Spawns.Select(s => s.SourceEffectId).Distinct().ShouldBe(
            new[] { SummonEffect }, Case.Sensitive);
    }

    /// <summary>
    /// 🔴 <b>`05` §3.1 — <em>"summons take the next free id and never reuse a dead one"</em>.</b> The
    /// log <b>is</b> the replay, and two actors on one id would draw the second resuming the first's
    /// HP bar.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>This case previously carried this name and asserted nothing about a log id at all</b> —
    /// it read back the summoner id and the source effect id, both of which the fixture had just
    /// written, and its "two waves" were one phase-3 entry (a boss already in phase 3 enters no
    /// second one). Steering S1.
    /// </para>
    /// <para>
    /// The only shape that can observe the rule is a <b>recurring</b> summon with a death between
    /// two firings, so the mechanic here is a 2 s <c>PERIODIC</c>: phase 3 is entered at tick 60,
    /// R8 anchors there, and firings land at 100, 140 and 180. The first add is killed at tick 150,
    /// freeing a slot under the cap — and the add that replaces it must take log id <b>4</b>, not the
    /// dead one's <b>1</b>.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_add_replacing_a_dead_one_takes_a_new_log_id_and_never_the_dead_ones()
    {
        var summons = new RecordingSummons(Add());

        var run = WaveFight(
            summons,
            (Tick: 60, ActorId: "BOSS_THORNMAW", Fraction: 0.20),
            (Tick: 150, ActorId: "ADD_SWARM_1", Fraction: 0.0));

        summons.Spawns.Count.ShouldBe(
            4,
            "the floor: 2 at tick 100, a third at 140 (the cap admits one more), and a FOURTH at 180 " +
            "only because the first one died — without four spawns the id claim below is vacuous");

        var adds = run.Driver.Actors.Where(a => a.IsSummon).ToArray();

        adds.Length.ShouldBe(4, "and all four were admitted to the roster");

        adds.Single(a => string.Equals(a.Id, "ADD_SWARM_1", StringComparison.Ordinal)).IsAlive
            .ShouldBeFalse("the floor: the first add really is dead when the fourth spawns");

        adds.Select(a => a.LogId).ShouldBe(
            new byte[]
            {
                CombatActor.Enemy(1), CombatActor.Enemy(2), CombatActor.Enemy(3), CombatActor.Enemy(4),
            },
            "🔒 the next free id every time — the fourth add takes 4 and NOT the dead first add's 1");

        adds.Select(a => a.LogId).Distinct().Count().ShouldBe(4, "and no two share one");
        adds.Select(a => a.LogId).ShouldNotContain(
            CombatActor.Enemy(0), "nor does any of them take the boss's");
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

    /// <summary>
    /// 🔴 And the cap <b>holds in a fight</b>: a recurring summon that asks for two adds a firing
    /// never puts a fourth on the roster while three are alive.
    /// </summary>
    /// <remarks>
    /// The constant above is a number; this is the rule. Three firings at ticks 100, 140 and 180 ask
    /// for six adds between them, and `17` §1's cap admits three — the third firing spawns nothing
    /// at all. Without the per-firing floor a cap that admitted <em>none</em> would satisfy
    /// "never more than three" perfectly (steering S3).
    /// </remarks>
    [Fact]
    public void A_firing_while_three_adds_are_alive_spawns_nothing()
    {
        var summons = new RecordingSummons(Add());

        var run = WaveFight(summons, (Tick: 60, ActorId: "BOSS_THORNMAW", Fraction: 0.20));

        summons.Spawns.Count.ShouldBe(
            BossAdds.MaxAlive,
            "three firings asked for six adds; 17 §1 admits three, and the third firing — with three " +
            "already alive — reaches the roster half not at all");

        var adds = run.Driver.Actors.Where(a => a.IsSummon).ToArray();

        adds.Length.ShouldBe(BossAdds.MaxAlive, "the floor: adds DID spawn, and exactly the cap");
        adds.Count(a => a.IsAlive).ShouldBe(BossAdds.MaxAlive, "and all three are alive at the end");
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

    /// <summary>
    /// `17` §2's adds on a <b>recurring</b> cadence — the only shape in which a second wave exists,
    /// and therefore the only one that can observe `05` §3.1's <em>"never reuse a dead one's id"</em>
    /// and `17` §1's cap refusing a firing.
    /// </summary>
    private static EffectDefinition WaveSummon() =>
        BossTestBench.Summon(SummonEffect, count: 2.0, everySeconds: 2.0);

    private static ActorPlan WaveBossPlan() =>
        BossTestBench.Boss(
            "BOSS_THORNMAW",
            maxHp: 1000.0,
            BossTestBench.InPhase("BOSS_THORNMAW", 3, WaveSummon()));

    private static BossRun WaveFight(
        RecordingSummons summons, params (int Tick, string ActorId, double Fraction)[] script) =>
        BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), WaveBossPlan() },
            new List<BossEncounter>
            {
                Encounter() with { Plan = WaveBossPlan() },
            },
            new List<EffectInstanceId> { EffectInstanceId.Of(SummonInstance) },
            script,
            summons: summons,
            maxTicks: 220);
}
