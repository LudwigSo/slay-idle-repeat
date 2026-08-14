using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Conditions;

/// <summary>
/// 🔒 `18` §4 — what each of the twenty-three condition functions reads.
/// </summary>
public sealed class ConditionFunctionTests
{
    private static double Read(
        ConditionFunction function,
        EffectEvaluationContext context,
        ConditionArguments arguments = default) =>
        ConditionEvaluator.Read(function, arguments, context);

    // ------------------------------------------------------------------ HP

    /// <summary>`18` §4 — <c>SELF_HP_PCT</c> and <c>SELF_MISSING_HP_PCT</c> are 0..1 and complementary.</summary>
    [Fact]
    public void SELF_HP_PCT_and_SELF_MISSING_HP_PCT_read_the_holder()
    {
        var hero = EffectTestBattle.Hero(currentHp: 40, maxHp: 100);
        var battle = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1));

        Read(ConditionFunction.SELF_HP_PCT, battle).ShouldBe(0.40);
        Read(ConditionFunction.SELF_MISSING_HP_PCT, battle).ShouldBe(0.60);
    }

    /// <summary>
    /// 🔒 The reading is rounded to four decimal places before it leaves the evaluator (`05` §1.1,
    /// `14` §8.2, `18` §1.1) — the accumulation point a comparison and a <c>valueScale</c> division
    /// both read.
    /// </summary>
    [Fact]
    public void An_HP_fraction_is_rounded_to_four_decimal_places()
    {
        var hero = EffectTestBattle.Hero(currentHp: 1, maxHp: 3);
        var battle = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1));

        // 1/3 is 0.3333333333333333 unrounded. Asserted as an exact equality, not a tolerance:
        // the point of the rule is that every platform produces the SAME double here.
        Read(ConditionFunction.SELF_HP_PCT, battle).ShouldBe(0.3333);
        Read(ConditionFunction.SELF_MISSING_HP_PCT, battle).ShouldBe(0.6667);
    }

    /// <summary>`18` §7.2 — <c>PK_EXECUTIONER</c> reads the holder's current target.</summary>
    [Fact]
    public void TARGET_HP_PCT_reads_the_current_target()
    {
        var hero = EffectTestBattle.Hero(currentHp: 100, maxHp: 100);
        var wounded = EffectTestBattle.Enemy("GRUNT_WOUNDED", 1, currentHp: 25, maxHp: 100);
        var healthy = EffectTestBattle.Enemy("GRUNT_HEALTHY", 2);

        var battle = EffectTestBattle.Context(hero, hero, wounded, healthy) with
        {
            CurrentTarget = wounded,
        };

        Read(ConditionFunction.TARGET_HP_PCT, battle).ShouldBe(0.25);
    }

    /// <summary>
    /// A zero <c>MaxHp</c> has no HP fraction, and `18` §4 authors no answer for one. Steering S6:
    /// fail loudly rather than return the 0 or the 1 that a division guard would invent.
    /// </summary>
    /// <remarks>
    /// All three HP-fraction functions divide by the same denominator, so all three are asserted —
    /// a guard on one of them is not a rule.
    /// </remarks>
    [Theory]
    [InlineData(ConditionFunction.SELF_HP_PCT)]
    [InlineData(ConditionFunction.SELF_MISSING_HP_PCT)]
    [InlineData(ConditionFunction.TARGET_HP_PCT)]
    public void An_HP_fraction_over_a_zero_MaxHp_fails_loudly(ConditionFunction function)
    {
        var hero = EffectTestBattle.Hero(currentHp: 0, maxHp: 0);
        var target = EffectTestBattle.Enemy("GRUNT_A", 1, currentHp: 0, maxHp: 0);

        var battle = EffectTestBattle.Context(hero, hero, target) with { CurrentTarget = target };

        Should.Throw<EffectContextException>(() => Read(function, battle))
            .Token.ShouldBe(function.ToString());
    }

    /// <summary>
    /// 🔒 `18` §4 types the HP functions <c>0..1</c>, and the reading is clamped to that range.
    /// </summary>
    /// <remarks>
    /// Two live paths break it: `05` §4 step 9 applies no floor at zero and §3.1 defers <em>removal</em>
    /// to the death slot, so an overkilled holder firing its <c>ON_DEATH</c> effect sits at negative HP;
    /// and a Max HP <b>decrease</b> leaves current above maximum.
    /// <para>
    /// ⚠️ Unclamped, the second is the damaging one: <c>SELF_MISSING_HP_PCT</c> reads <c>-0.2</c> and
    /// <c>PK_BERSERK</c>'s scale floors that to <b>-20 steps</b> — a perk that only ever adds ATK
    /// subtracting 20% of it. <see cref="ValueScale.StepsFor"/> imposes no lower bound precisely because
    /// it is told every §4 function is non-negative by construction; this is what makes that true.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_HP_fraction_is_clamped_to_the_zero_to_one_range_18_declares()
    {
        // Overkilled: 05 §4 applies no HP floor, and ON_DEATH fires before removal.
        var overkilled = EffectTestBattle.Hero(currentHp: -40, maxHp: 100);
        var dying = EffectTestBattle.Context(overkilled, overkilled, EffectTestBattle.Enemy("GRUNT_A", 1));

        Read(ConditionFunction.SELF_HP_PCT, dying).ShouldBe(0.0);
        Read(ConditionFunction.SELF_MISSING_HP_PCT, dying).ShouldBe(1.0);

        // Max HP fell below current — a +20% Max HP buff expiring at full health.
        var overfull = EffectTestBattle.Hero(currentHp: 900, maxHp: 750);
        var rebased = EffectTestBattle.Context(overfull, overfull, EffectTestBattle.Enemy("GRUNT_A", 1));

        Read(ConditionFunction.SELF_HP_PCT, rebased).ShouldBe(1.0);

        var missing = Read(ConditionFunction.SELF_MISSING_HP_PCT, rebased);
        missing.ShouldBe(0.0);

        new ValueScale { Fn = ConditionFunction.SELF_MISSING_HP_PCT, Per = 0.01, Cap = 45 }
            .StepsFor(missing)
            .ShouldBe(0, "unclamped this reads -0.2 and PK_BERSERK applies -20% ATK");
    }

    // ------------------------------------------------------------------ the roster

    /// <summary>
    /// 🔒 <c>ENEMY_COUNT</c> is holder-relative, exactly as `18` §5's target tokens are (the R10
    /// ruling): on a boss it counts the hero side.
    /// </summary>
    [Fact]
    public void ENEMY_COUNT_counts_the_living_non_pets_hostile_to_the_holder()
    {
        var hero = EffectTestBattle.Hero();
        var heroPet = EffectTestBattle.Pet("PET_STORMFANG", 1);
        var boss = EffectTestBattle.Enemy("BOSS_THORNMAW", 2) with { IsBoss = true };
        var swarmA = EffectTestBattle.Enemy("SUMMON_SWARM_A", 3) with { IsSummon = true };
        var dead = EffectTestBattle.Enemy("SUMMON_SWARM_B", 4, currentHp: 0) with { IsAlive = false };

        var roster = new IEffectActorView[] { hero, heroPet, boss, swarmA, dead };

        var fromTheHero = EffectTestBattle.Context(hero, roster);
        Read(ConditionFunction.ENEMY_COUNT, fromTheHero).ShouldBe(
            2,
            "the boss and the living swarm; the dead one and the hero's own pet are not enemies");

        var fromTheBoss = EffectTestBattle.Context(boss, roster);
        Read(ConditionFunction.ENEMY_COUNT, fromTheBoss).ShouldBe(
            1,
            "18 §7.10's R10 reading: the boss's enemies are the hero side, and 05 §3.2 keeps pets out");
    }

    /// <summary>`18` §4.</summary>
    [Fact]
    public void TARGET_IS_ELITE_and_TARGET_IS_BOSS_read_the_current_target()
    {
        var hero = EffectTestBattle.Hero();
        var elite = EffectTestBattle.Enemy("ELITE_A", 1) with { IsElite = true };
        var boss = EffectTestBattle.Enemy("BOSS_A", 2) with { IsBoss = true };
        var grunt = EffectTestBattle.Enemy("GRUNT_A", 3);
        var roster = new IEffectActorView[] { hero, elite, boss, grunt };

        Read(ConditionFunction.TARGET_IS_ELITE, EffectTestBattle.Context(hero, roster) with { CurrentTarget = elite })
            .ShouldBe(1);
        Read(ConditionFunction.TARGET_IS_BOSS, EffectTestBattle.Context(hero, roster) with { CurrentTarget = elite })
            .ShouldBe(0);

        Read(ConditionFunction.TARGET_IS_BOSS, EffectTestBattle.Context(hero, roster) with { CurrentTarget = boss })
            .ShouldBe(1);
        Read(ConditionFunction.TARGET_IS_ELITE, EffectTestBattle.Context(hero, roster) with { CurrentTarget = boss })
            .ShouldBe(0);

        Read(ConditionFunction.TARGET_IS_ELITE, EffectTestBattle.Context(hero, roster) with { CurrentTarget = grunt })
            .ShouldBe(0);
    }

    // ------------------------------------------------------------------ the clock

    /// <summary>`18` §4 — <c>BATTLE_TIME</c> is seconds elapsed, straight from the context.</summary>
    [Fact]
    public void BATTLE_TIME_is_the_elapsed_battle_time()
    {
        var hero = EffectTestBattle.Hero();
        var battle = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1)) with
        {
            BattleTimeSeconds = 12.35,
        };

        Read(ConditionFunction.BATTLE_TIME, battle).ShouldBe(12.35);
    }

    /// <summary>
    /// 🔒 The two clock readings are rounded to four places as well — they are the ones a 20 Hz tick
    /// accumulates, and therefore the ones most likely to arrive with a float tail.
    /// </summary>
    /// <remarks>
    /// `05` §3's <c>TICK = 0.05 s</c> is not representable in binary floating point, so 1 800 of them
    /// summed is not 90. Rounding only the HP pair would leave the clock as the one accumulation
    /// point in `18` §4 that could put a <c>valueScale</c> step boundary in a different place on two
    /// devices (`14` §8.2).
    /// </remarks>
    [Fact]
    public void The_clock_readings_are_rounded_to_four_decimal_places()
    {
        var hero = EffectTestBattle.Hero();

        // 0.05 summed 247 times. The exact double is 12.350000000000005, not 12.35.
        var accumulated = 0.0;
        for (var tick = 0; tick < 247; tick++)
        {
            accumulated += 0.05;
        }

        accumulated.ShouldNotBe(12.35, "the premise: 0.05 is not representable, so the sum drifts");

        var battle = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1)) with
        {
            BattleTimeSeconds = accumulated,
            FightHorizonSeconds = EffectTestBattle.PveTimeoutSeconds,
        };

        Read(ConditionFunction.BATTLE_TIME, battle).ShouldBe(12.35);
        Read(ConditionFunction.BATTLE_TIME_REMAINING_EST, battle).ShouldBe(77.65);
    }

    /// <summary>
    /// 🔒 `18` §4 says <c>BATTLE_TIME_REMAINING_EST</c> is <em>"seconds to the 70 s enrage"</em>. `05`
    /// §3.1 says the enrage is <em>"Bosses only; ordinary fights rely on the 90 s timeout"</em>.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The ruling</b>, since `18` gives no answer for a fight with no enrage:
    /// <c>max(0, horizon − elapsed)</c>, where the horizon is the <b>enrage when the fight has one and
    /// the fight's forced end otherwise</b>. Both numbers are readings on the context, never constants
    /// here — <c>70</c>, <c>90</c> and the duel's <c>60</c> are tunables (`05` §3, §3.3, `17` §1,
    /// `11` §4.3) and `21` §3.1 keeps tunables out of code.
    /// </remarks>
    [Theory]
    // A boss fight: the enrage is the horizon, and the 90 s timeout is not.
    [InlineData(10.0, EffectTestBattle.EnrageSeconds, EffectTestBattle.PveTimeoutSeconds, 60.0)]
    [InlineData(69.5, EffectTestBattle.EnrageSeconds, EffectTestBattle.PveTimeoutSeconds, 0.5)]
    // 🔒 Past the enrage there is no time remaining. Clamped rather than negative: a negative reading
    // would flip the sign of every valueScale step driven by it (18 §1.1).
    [InlineData(71.0, EffectTestBattle.EnrageSeconds, EffectTestBattle.PveTimeoutSeconds, 0.0)]
    // An ordinary fight has no enrage, so the horizon is 05 §3's 90 s timeout.
    [InlineData(10.0, null, EffectTestBattle.PveTimeoutSeconds, 80.0)]
    [InlineData(89.95, null, EffectTestBattle.PveTimeoutSeconds, 0.05)]
    // A duel has no enrage and 05 §3.3's 60 s cap.
    [InlineData(10.0, null, EffectTestBattle.PvpTimeoutSeconds, 50.0)]
    public void BATTLE_TIME_REMAINING_EST_counts_down_to_the_enrage_or_the_fights_end(
        double elapsed,
        double? enrageAt,
        double horizon,
        double expected)
    {
        var hero = EffectTestBattle.Hero();
        var battle = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1)) with
        {
            BattleTimeSeconds = elapsed,
            EnrageAtSeconds = enrageAt,
            FightHorizonSeconds = horizon,
        };

        Read(ConditionFunction.BATTLE_TIME_REMAINING_EST, battle).ShouldBe(expected);
    }

    // ------------------------------------------------------------------ statuses

    /// <summary>
    /// `18` §4 — <c>HAS_STATUS</c> (<em>"bool, by status id"</em>) and <c>STATUS_STACKS</c>
    /// (<em>"int"</em>) read the <b>holder</b>.
    /// </summary>
    /// <remarks>
    /// ⚠️ `18` §4 gives neither a prefix nor a worked example, so whose status these read is an
    /// assumption — recorded as such, and made on the grounds that every §4 function that reads
    /// somebody else says so in its name (<c>TARGET_*</c>, <c>ATTACKER_*</c>).
    /// </remarks>
    [Fact]
    public void HAS_STATUS_and_STATUS_STACKS_read_the_holders_own_statuses()
    {
        var hero = EffectTestBattle.Hero() with
        {
            Statuses = new Dictionary<string, int>(StringComparer.Ordinal) { ["SUNDER"] = 3 },
        };

        // The target is drowning in stacks. If either function read the target rather than the
        // holder, the numbers below would be 5 and 1 instead of 3 and 0.
        var target = EffectTestBattle.Enemy("GRUNT_A", 1) with
        {
            Statuses = new Dictionary<string, int>(StringComparer.Ordinal) { ["SUNDER"] = 5, ["BURN"] = 5 },
        };

        var battle = EffectTestBattle.Context(hero, hero, target) with { CurrentTarget = target };

        Read(ConditionFunction.STATUS_STACKS, battle, new ConditionArguments("SUNDER", null, null)).ShouldBe(3);
        Read(ConditionFunction.HAS_STATUS, battle, new ConditionArguments("SUNDER", null, null)).ShouldBe(1);

        Read(ConditionFunction.STATUS_STACKS, battle, new ConditionArguments("BURN", null, null)).ShouldBe(0);
        Read(ConditionFunction.HAS_STATUS, battle, new ConditionArguments("BURN", null, null)).ShouldBe(0);
    }

    /// <summary>🔒 Status ids compare ordinally, as every id in this repository (`14` §8.2).</summary>
    [Fact]
    public void A_status_id_is_matched_ordinally()
    {
        var hero = EffectTestBattle.Hero() with
        {
            Statuses = new Dictionary<string, int>(StringComparer.Ordinal) { ["SUNDER"] = 3 },
        };

        var battle = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1));

        Read(ConditionFunction.HAS_STATUS, battle, new ConditionArguments("sunder", null, null))
            .ShouldBe(0, "a differently-cased id is a different status, not the same one");
    }

    // ------------------------------------------------------------------ the run

    /// <summary>The nine `18` §4 functions that read run state, read it through <c>IRunStateView</c>.</summary>
    [Fact]
    public void The_run_state_functions_read_the_run_view()
    {
        var run = EffectTestBattle.Run() with
        {
            PerksByCategory = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["OFFENSE"] = 4,
                ["DEFENSE"] = 2,
                ["ECONOMY"] = 1,
            },
            DieFacesByKind = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["Star"] = 2,
                ["Pip"] = 16,
            },
            PetCount = 3,
            GoldHeld = 1_450,
            BattlesWonThisRun = 6,
            StageIndex = 2,
            Chapter = 5,
            Tier = 3,
        };

        var hero = EffectTestBattle.Hero();
        var battle = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1)) with
        {
            Run = run,
        };

        Read(ConditionFunction.PERK_COUNT, battle).ShouldBe(7, "no category means every perk held");
        Read(ConditionFunction.PERK_COUNT, battle, new ConditionArguments(null, "OFFENSE", null)).ShouldBe(4);
        Read(ConditionFunction.PERK_COUNT, battle, new ConditionArguments(null, "DICE_AND_BOARD", null))
            .ShouldBe(0, "a category the run holds none of is a reading, not an error");
        Read(ConditionFunction.DISTINCT_PERK_CATEGORIES, battle).ShouldBe(3);
        Read(ConditionFunction.PET_COUNT, battle).ShouldBe(3);
        Read(ConditionFunction.DIE_FACE_COUNT, battle, new ConditionArguments(null, null, "Star")).ShouldBe(2);
        Read(ConditionFunction.DIE_FACE_COUNT, battle, new ConditionArguments(null, null, "Void")).ShouldBe(0);
        Read(ConditionFunction.GOLD_HELD, battle).ShouldBe(1_450);
        Read(ConditionFunction.BATTLES_WON_THIS_RUN, battle).ShouldBe(6);
        Read(ConditionFunction.STAGE_INDEX, battle).ShouldBe(2);
        Read(ConditionFunction.CHAPTER, battle).ShouldBe(5);
        Read(ConditionFunction.TIER, battle).ShouldBe(3);
    }

    // ------------------------------------------------------------------ the attacker trio

    /// <summary>
    /// `18` §7.10 — <c>PK_STALWART</c>'s <em>"−20% damage taken from Elites and Bosses"</em>, read in
    /// a context that has an attacker.
    /// </summary>
    [Fact]
    public void The_ATTACKER_IS_trio_reads_the_attacker_when_there_is_one()
    {
        var hero = EffectTestBattle.Hero();
        var elite = EffectTestBattle.Enemy("ELITE_A", 1) with { IsElite = true };
        var boss = EffectTestBattle.Enemy("BOSS_A", 2) with { IsBoss = true };
        var sporeling = EffectTestBattle.Enemy("SUMMON_SPORELING", 3) with
        {
            IsSummon = true,
            OwnerId = "BOSS_A",
        };
        var roster = new IEffectActorView[] { hero, elite, boss, sporeling };

        var hitByTheElite = EffectTestBattle.Context(hero, roster) with { Attacker = elite };
        Read(ConditionFunction.ATTACKER_IS_ELITE, hitByTheElite).ShouldBe(1);
        Read(ConditionFunction.ATTACKER_IS_BOSS, hitByTheElite).ShouldBe(0);
        Read(ConditionFunction.ATTACKER_IS_SUMMON, hitByTheElite).ShouldBe(0);

        var hitByTheBoss = EffectTestBattle.Context(hero, roster) with { Attacker = boss };
        Read(ConditionFunction.ATTACKER_IS_BOSS, hitByTheBoss).ShouldBe(1);

        var hitByASporeling = EffectTestBattle.Context(hero, roster) with { Attacker = sporeling };
        Read(ConditionFunction.ATTACKER_IS_SUMMON, hitByASporeling).ShouldBe(1);
        Read(ConditionFunction.ATTACKER_IS_ELITE, hitByASporeling).ShouldBe(0);
    }

    /// <summary>
    /// 🔒 `18` §4 — <em>"valid only in contexts with an attacker (<c>ON_HIT_TAKEN</c>,
    /// <c>ON_DODGE</c>/<c>ON_BLOCK</c>, and <c>DAMAGE_TAKEN_MULT</c> evaluation inside `05` §4 step
    /// 6); <b><c>false</c> elsewhere</b>"</em>.
    /// </summary>
    /// <remarks>
    /// An <b>authored default</b>, and the only one `18` §4 writes — which is why the three functions
    /// below return <c>false</c> where every other absent subject throws. <c>PK_STALWART</c> is an
    /// <c>ALWAYS</c> effect, so it is evaluated at every resolution pass including the stat
    /// aggregation, where no attacker exists; a throw there would make the perk unusable.
    /// </remarks>
    [Theory]
    [InlineData(ConditionFunction.ATTACKER_IS_ELITE)]
    [InlineData(ConditionFunction.ATTACKER_IS_BOSS)]
    [InlineData(ConditionFunction.ATTACKER_IS_SUMMON)]
    public void The_ATTACKER_IS_trio_is_false_outside_an_attacker_context(ConditionFunction function)
    {
        var hero = EffectTestBattle.Hero();
        var noAttacker = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("ELITE_A", 1) with { IsElite = true });

        noAttacker.Attacker.ShouldBeNull();

        Read(function, noAttacker).ShouldBe(0);
    }

    // ------------------------------------------------------------------ the worked examples

    /// <summary>
    /// `18` §7.2 — <c>PK_EXECUTIONER</c> Tier I, verbatim:
    /// <c>{"fn":"TARGET_HP_PCT","op":"lt","value":0.30}</c>.
    /// </summary>
    [Fact]
    public void PK_EXECUTIONER_gates_on_the_targets_HP_fraction()
    {
        var executioner = EffectCondition.Of(new ConditionTerm
        {
            Fn = ConditionFunction.TARGET_HP_PCT,
            Comparator = ConditionComparator.LT,
            Value = 0.30,
        });

        var hero = EffectTestBattle.Hero();
        var wounded = EffectTestBattle.Enemy("GRUNT_WOUNDED", 1, currentHp: 29, maxHp: 100);
        var healthy = EffectTestBattle.Enemy("GRUNT_HEALTHY", 2, currentHp: 31, maxHp: 100);

        ConditionEvaluator.IsSatisfied(
            executioner,
            EffectTestBattle.Context(hero, hero, wounded) with { CurrentTarget = wounded })
            .ShouldBeTrue();

        ConditionEvaluator.IsSatisfied(
            executioner,
            EffectTestBattle.Context(hero, hero, healthy) with { CurrentTarget = healthy })
            .ShouldBeFalse();
    }

    /// <summary>
    /// `18` §7.10 — <c>PK_STALWART</c> Tier I, verbatim: an <c>any</c> over
    /// <c>ATTACKER_IS_ELITE</c> and <c>ATTACKER_IS_BOSS</c>, both compared against the boolean
    /// <c>true</c>.
    /// </summary>
    [Fact]
    public void PK_STALWART_fires_against_an_elite_or_a_boss_and_against_nobody_else()
    {
        var stalwart = EffectCondition.Any(
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.ATTACKER_IS_ELITE,
                Comparator = ConditionComparator.EQ,
                Flag = true,
            }),
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.ATTACKER_IS_BOSS,
                Comparator = ConditionComparator.EQ,
                Flag = true,
            }));

        var hero = EffectTestBattle.Hero();
        var elite = EffectTestBattle.Enemy("ELITE_A", 1) with { IsElite = true };
        var boss = EffectTestBattle.Enemy("BOSS_A", 2) with { IsBoss = true };
        var grunt = EffectTestBattle.Enemy("GRUNT_A", 3);
        var roster = new IEffectActorView[] { hero, elite, boss, grunt };

        ConditionEvaluator.IsSatisfied(stalwart, EffectTestBattle.Context(hero, roster) with { Attacker = elite })
            .ShouldBeTrue();
        ConditionEvaluator.IsSatisfied(stalwart, EffectTestBattle.Context(hero, roster) with { Attacker = boss })
            .ShouldBeTrue();
        ConditionEvaluator.IsSatisfied(stalwart, EffectTestBattle.Context(hero, roster) with { Attacker = grunt })
            .ShouldBeFalse();
        ConditionEvaluator.IsSatisfied(stalwart, EffectTestBattle.Context(hero, roster))
            .ShouldBeFalse("18 §4's authored default is false, so an ungated pass is not the answer either");
    }

    // ------------------------------------------------------------------ the M2-06 seam

    /// <summary>
    /// 🔒 `18` §1.1 — <c>PK_BERSERK</c> Tier I: <em>"+1% ATK per 1% missing HP, up to +45%"</em>,
    /// <c>{"fn":"SELF_MISSING_HP_PCT","per":0.01,"cap":45}</c>.
    /// </summary>
    /// <remarks>
    /// The seam M2-06 plugs into, asserted end to end: <see cref="ConditionEvaluator.Read"/> supplies
    /// the reading already rounded to 4 dp, and <see cref="ValueScale.StepsFor"/> does the division.
    /// 🔒 The order is load-bearing — `18` §1.1 rounds <b>before</b> the division, and an unrounded
    /// <c>0.44999999999999996</c> floors to 44 steps where the rounded <c>0.45</c> floors to 45.
    /// </remarks>
    [Fact]
    public void PK_BERSERK_scales_off_a_reading_that_is_already_rounded_to_four_places()
    {
        var berserk = new ValueScale
        {
            Fn = ConditionFunction.SELF_MISSING_HP_PCT,
            Per = 0.01,
            Cap = 45,
        };

        // 🔒 A hero at 55/100 is the case the rounding rule exists for: `1 - 0.55` is
        // 0.44999999999999996 in binary floating point, which floors to 44 steps unrounded and to
        // the cap's 45 once rounded. One step of ATK, decided by whether the rounding happened.
        (1.0 - (55.0 / 100.0)).ShouldNotBe(0.45, "the premise: the raw subtraction is 0.44999999999999996");

        var hero = EffectTestBattle.Hero(currentHp: 55, maxHp: 100);
        var battle = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1));

        var reading = Read(ConditionFunction.SELF_MISSING_HP_PCT, battle);
        reading.ShouldBe(0.45);
        berserk.StepsFor(reading).ShouldBe(45);
        berserk.EffectiveValue(0.01, reading).ShouldBe(0.45);

        // At full HP the reading is 0 steps — and 18 §8 step 10's rounding must give +0.0, never
        // -0.0, because CanonicalStateWriter THROWS on a negative zero rather than encoding one.
        // 18 §7.10's Bog Air is an authored negative value, so this is the live case, not a curio.
        var full = EffectTestBattle.Context(
            EffectTestBattle.Hero(),
            EffectTestBattle.Hero(),
            EffectTestBattle.Enemy("GRUNT_A", 1));

        var atFullHp = Read(ConditionFunction.SELF_MISSING_HP_PCT, full);
        berserk.StepsFor(atFullHp).ShouldBe(0);

        double.IsNegative(berserk.EffectiveValue(-0.35, atFullHp)).ShouldBeFalse(
            "zero steps of a negative authored value must be +0.0");
    }

    /// <summary>
    /// `18` §1.1 — <c>PK_HOARD</c>: <em>"+1% ATK per 100 Gold currently held"</em>, uncapped.
    /// </summary>
    [Fact]
    public void PK_HOARD_scales_off_the_run_views_gold()
    {
        var hoard = new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = 100, Cap = null };

        var hero = EffectTestBattle.Hero();
        var battle = EffectTestBattle.Context(hero, hero, EffectTestBattle.Enemy("GRUNT_A", 1)) with
        {
            Run = EffectTestBattle.Run() with { GoldHeld = 1_450 },
        };

        hoard.StepsFor(Read(ConditionFunction.GOLD_HELD, battle)).ShouldBe(14);
    }
}
