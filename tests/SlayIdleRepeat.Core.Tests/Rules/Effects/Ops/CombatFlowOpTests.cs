using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>
/// Combat-flow ops other than <c>STAT_COPY</c> (see <c>StatCopyOpTests</c>) and <c>RANDOM_OUTCOME</c>
/// (see <c>RandomOutcomeOpTests</c>).
/// </summary>
public sealed class CombatFlowOpTests
{
    /// <summary><c>PK_FLURRY</c>: one extra attack on the current target.</summary>
    [Fact]
    public void EXTRA_ATTACK_takes_its_count_from_value_which_is_what_18_7_3_authors()
    {
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("EN_1", 1);
        var bench = new OpTestBench();

        var flurry = OpFixtures.Effect("PK_FLURRY_T1", EffectOp.EXTRA_ATTACK, 1.0, EffectTarget.CURRENT_TARGET);

        var evaluation = EffectTestBattle.Context(hero, hero, enemy) with { CurrentTarget = enemy };
        CombatFlowOps.ExtraAttack(flurry, bench.Context(evaluation));

        bench.Calls.ShouldBe(["ExtraAttack(HERO->EN_1, 1, PK_FLURRY_T1)"], Case.Sensitive);
    }

    /// <summary>A fractional count is refused: 1.9 extra attacks is not one, and not two.</summary>
    [Theory]
    [InlineData(1.5)]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void EXTRA_ATTACK_refuses_a_count_that_is_not_a_whole_number_of_attacks(double value)
    {
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("EN_1", 1);
        var bench = new OpTestBench();

        var effect = OpFixtures.Effect("PK_X", EffectOp.EXTRA_ATTACK, value, EffectTarget.CURRENT_TARGET);
        var evaluation = EffectTestBattle.Context(hero, hero, enemy) with { CurrentTarget = enemy };

        Should.Throw<EffectContextException>(
                  () => CombatFlowOps.ExtraAttack(effect, bench.Context(evaluation)))
              .Message.ShouldContain("whole actors and whole attacks", Case.Sensitive);

        bench.Calls.ShouldBeEmpty();
    }

    /// <summary>
    /// <c>PK_OPENER</c>'s "×3 first attack": the multiplier is <c>value</c>, the count is
    /// <c>charges</c>. Holder-scoped.
    /// </summary>
    [Fact]
    public void ATTACK_MULT_NEXT_grants_charges_of_its_value_onto_the_holder()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var opener = OpFixtures.Effect("PK_OPENER_T1", EffectOp.ATTACK_MULT_NEXT, 3.0) with { Charges = 1 };

        CombatFlowOps.AttackMultiplierCharges(opener, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.Calls.ShouldBe(
            ["GrantAttackMultiplierCharges(HERO, 3, PK_OPENER_T1)", "charges=1"],
            Case.Sensitive,
            "05 §4 consumes ATTACK_MULT_NEXT charges in ascending effect-id order, so both travel");
    }

    /// <summary>Without <c>charges</c> the op cannot say how many attacks it covers, and is refused.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    public void ATTACK_MULT_NEXT_without_a_positive_charges_count_is_refused(int? charges)
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var effect = OpFixtures.Effect("PK_X", EffectOp.ATTACK_MULT_NEXT, 3.0) with { Charges = charges };

        Should.Throw<EffectContextException>(
                  () => CombatFlowOps.AttackMultiplierCharges(
                      effect, bench.Context(EffectTestBattle.Context(hero, hero))))
              .Message.ShouldContain("the next N attacks", Case.Sensitive);

        bench.Calls.ShouldBeEmpty();
    }

    /// <summary><c>FORCE_CRIT_NEXT</c> carries the count and nothing else.</summary>
    [Fact]
    public void FORCE_CRIT_NEXT_grants_charges_and_carries_no_value_at_all()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var guaranteed = OpFixtures.Effect("PK_SURE_STRIKE", EffectOp.FORCE_CRIT_NEXT) with { Charges = 2 };

        CombatFlowOps.ForcedCritCharges(guaranteed, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.OnlyAmount("GrantForcedCritCharges").ShouldBe(2.0);
    }

    /// <summary>
    /// A value on <c>FORCE_CRIT_NEXT</c> is refused rather than ignored — <c>{"charges": 2,
    /// "value": 3}</c> reads as "three attacks" to whoever wrote it, and silently dropping the 3
    /// would ship that misreading.
    /// </summary>
    [Fact]
    public void FORCE_CRIT_NEXT_refuses_a_value_rather_than_ignoring_it()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var confused = OpFixtures.Effect("PK_X", EffectOp.FORCE_CRIT_NEXT, 3.0) with { Charges = 2 };

        Should.Throw<EffectContextException>(
                  () => CombatFlowOps.ForcedCritCharges(
                      confused, bench.Context(EffectTestBattle.Context(hero, hero))))
              .Message.ShouldContain("carries a value", Case.Sensitive);

        bench.Calls.ShouldBeEmpty();
    }

    /// <summary><c>REDUCE_COOLDOWN</c> is a fraction of the cooldown, not a flat amount.</summary>
    [Fact]
    public void REDUCE_COOLDOWN_is_a_fraction_of_the_cooldown()
    {
        var hero = EffectTestBattle.Hero();
        var pet = EffectTestBattle.Pet("PET_STORMFANG", 1);
        var bench = new OpTestBench();

        var relentless = OpFixtures.Effect(
            "TAL_RELENTLESS", EffectOp.REDUCE_COOLDOWN, 0.15, EffectTarget.ALL_PETS);

        CombatFlowOps.ReduceCooldown(relentless, bench.Context(EffectTestBattle.Context(hero, hero, pet)));

        bench.Calls.ShouldBe(["ReduceCooldowns(PET_STORMFANG, 0.15, TAL_RELENTLESS)"], Case.Sensitive);
    }

    /// <summary>
    /// <c>PK_UNBREAKABLE</c> is <c>{"value": 1, "valueMode": "FLAT"}</c> — "survive at 1 HP". Under
    /// the default "HP fraction" reading the same <c>1</c> would mean full health instead.
    /// </summary>
    [Fact]
    public void SURVIVE_LETHAL_with_valueMode_FLAT_leaves_the_actor_at_1_HP_and_not_at_full_health()
    {
        var hero = EffectTestBattle.Hero(currentHp: 5, maxHp: 2400);
        var bench = new OpTestBench();

        var unbreakable = OpFixtures.Effect("PK_UNBREAKABLE_T1_SURVIVE", EffectOp.SURVIVE_LETHAL, 1.0) with
        {
            ValueMode = ValueMode.FLAT,
        };

        CombatFlowOps.SurviveLethal(unbreakable, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.OnlyAmount("ArmSurviveLethal").ShouldBe(
            1.0, "FLAT 1 is 1 HP; as a fraction of 2400 Max HP it would be the full 2400");
    }

    /// <summary>With no <c>valueMode</c>, the value is a fraction of Max HP.</summary>
    [Fact]
    public void SURVIVE_LETHAL_with_no_valueMode_is_a_fraction_of_max_HP()
    {
        var hero = EffectTestBattle.Hero(currentHp: 5, maxHp: 2400);
        var bench = new OpTestBench();

        var half = OpFixtures.Effect("BOSS_X_SURVIVE", EffectOp.SURVIVE_LETHAL, 0.25);

        CombatFlowOps.SurviveLethal(half, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.OnlyAmount("ArmSurviveLethal").ShouldBe(600.0, "0.25 x 2400");
    }

    /// <summary><c>REVIVE</c> is a fraction of the reviver's max HP.</summary>
    [Fact]
    public void REVIVE_returns_at_a_fraction_of_max_HP()
    {
        var hero = EffectTestBattle.Hero(currentHp: 0, maxHp: 1200);
        var bench = new OpTestBench();

        var revive = OpFixtures.Effect("PK_SECOND_WIND", EffectOp.REVIVE, 0.30);
        CombatFlowOps.Revive(revive, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.OnlyAmount("ArmRevive").ShouldBe(360.0);
    }

    /// <summary><c>REVIVE</c> admits no other <c>valueMode</c>; <c>FLAT</c> is refused rather than meaning 1 HP.</summary>
    [Fact]
    public void REVIVE_admits_no_value_mode_but_the_max_HP_fraction_18_2_4_words()
    {
        var hero = EffectTestBattle.Hero(currentHp: 0, maxHp: 1200);
        var bench = new OpTestBench();

        var flat = OpFixtures.Effect("PK_BAD_REVIVE", EffectOp.REVIVE, 1.0) with
        {
            ValueMode = ValueMode.FLAT,
        };

        Should.Throw<EffectContextException>(
                  () => CombatFlowOps.Revive(flat, bench.Context(EffectTestBattle.Context(hero, hero))))
              .Message.ShouldContain("does not admit valueMode FLAT", Case.Sensitive);

        bench.Calls.ShouldBeEmpty();
    }

    /// <summary>A save that leaves the actor at 0 HP has not saved anybody.</summary>
    [Fact]
    public void A_death_save_that_would_leave_the_actor_at_zero_HP_is_refused()
    {
        var hero = EffectTestBattle.Hero(maxHp: 1000);
        var bench = new OpTestBench();

        var broken = OpFixtures.Effect("PK_X", EffectOp.SURVIVE_LETHAL, 0.0) with { ValueMode = ValueMode.FLAT };

        Should.Throw<EffectContextException>(
                  () => CombatFlowOps.SurviveLethal(broken, bench.Context(EffectTestBattle.Context(hero, hero))))
              .Message.ShouldContain("0 is the state both ops exist to prevent", Case.Sensitive);
    }

    /// <summary>Thornmaw's periodic summon: two <c>SWARM</c>, at most three alive.</summary>
    [Fact]
    public void SUMMON_spawns_value_of_the_archetype_and_carries_maxAlive()
    {
        var boss = EffectTestBattle.Enemy("BOSS_THORNMAW", 1);
        var bench = new OpTestBench();

        var summon = OpFixtures.Effect("BOSS_THORNMAW_P3_SWARM", EffectOp.SUMMON, 2.0) with
        {
            Archetype = "SWARM",
            MaxAlive = 3,
        };

        CombatFlowOps.Summon(summon, bench.Context(EffectTestBattle.Context(boss, boss)));

        bench.Calls.ShouldBe(["Summon:SWARM(BOSS_THORNMAW, 2, BOSS_THORNMAW_P3_SWARM)", "maxAlive=3"], Case.Sensitive);
    }

    /// <summary><c>CLEAR_SUMMONS</c> defaults its target to <c>SELF</c>.</summary>
    [Fact]
    public void CLEAR_SUMMONS_defaults_to_SELF_which_is_the_one_target_default_18_authors()
    {
        var king = EffectTestBattle.Enemy("BOSS_OSSUARY_KING", 1);
        var bench = new OpTestBench();

        var riseAgain = OpFixtures.Effect("BOSS_OSSUARY_RISE_AGAIN", EffectOp.CLEAR_SUMMONS);

        CombatFlowOps.ClearSummons(riseAgain, bench.Context(EffectTestBattle.Context(king, king)));

        bench.Calls.ShouldBe(["ClearSummons(BOSS_OSSUARY_KING, 0, BOSS_OSSUARY_RISE_AGAIN)"], Case.Sensitive);
    }

    /// <summary>
    /// <c>SET_TARGET_PRIORITY</c>'s scale: <c>0</c> default, <c>-1</c> deprioritised, <c>+1</c> forced.
    /// </summary>
    [Theory]
    [InlineData(-1.0)]
    [InlineData(1.0)]
    public void SET_TARGET_PRIORITY_writes_the_authored_weight(double priority)
    {
        var queen = EffectTestBattle.Enemy("BOSS_SPOREQUEEN", 1);
        var bench = new OpTestBench();

        var effect = OpFixtures.Effect(
            "BOSS_SPOREQUEEN_SPORELING_PRIORITY", EffectOp.SET_TARGET_PRIORITY, priority, EffectTarget.SELF);

        CombatFlowOps.SetTargetPriority(effect, bench.Context(EffectTestBattle.Context(queen, queen)));

        bench.OnlyAmount("SetTargetPriority").ShouldBe(priority);
    }

    /// <summary><c>PK_STALWART</c>'s ×0.80 incoming damage.</summary>
    [Fact]
    public void DAMAGE_TAKEN_MULT_adds_its_multiplier_to_the_05_4_step_6_product()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var stalwart = OpFixtures.Effect(
            "PK_STALWART_T1", EffectOp.DAMAGE_TAKEN_MULT, 0.80, EffectTarget.SELF) with
        {
            Duration = new EffectDuration { Scope = DurationScope.BATTLE },
        };

        CombatFlowOps.DamageTakenMultiplier(stalwart, bench.Context(EffectTestBattle.Context(hero, hero)));

        bench.OnlyAmount("AddDamageTakenMultiplier").ShouldBe(0.80);
        bench.OnlyLifetime("AddDamageTakenMultiplier").Duration!.Scope.ShouldBe(DurationScope.BATTLE);
    }

    /// <summary>
    /// A negative multiplier would turn every hit into a heal, and zero would be permanent
    /// invulnerability — both refused rather than admitted.
    /// </summary>
    /// <remarks>Zero is reachable by accident via a <c>valueScale</c> whose step count comes out 0.</remarks>
    [Theory]
    [InlineData(-0.5)]
    [InlineData(0.0)]
    public void DAMAGE_TAKEN_MULT_refuses_a_multiplier_that_is_not_positive(double multiplier)
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var broken = OpFixtures.Effect("PK_X", EffectOp.DAMAGE_TAKEN_MULT, multiplier, EffectTarget.SELF);

        Should.Throw<EffectContextException>(
                  () => CombatFlowOps.DamageTakenMultiplier(
                      broken, bench.Context(EffectTestBattle.Context(hero, hero))))
              .Message.ShouldContain("permanent invulnerability", Case.Sensitive);

        bench.Calls.ShouldBeEmpty();
    }
}
