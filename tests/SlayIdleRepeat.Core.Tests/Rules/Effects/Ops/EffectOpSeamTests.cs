using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>
/// 🔒 <see cref="EffectOpSeams.Strict"/> — every unwired seam <b>throws naming the task that owns
/// it</b>, and none of them quietly does nothing.
/// </summary>
/// <remarks>
/// The defect this suite exists to prevent is specific and has a name in this project: a no-op
/// default turns "M2-09 has not landed" into "this perk does nothing", which the balance harness
/// (`05` §9) would attribute to the content and which no test would catch. So each case asserts the
/// <b>owner</b> in the message (steering S2), not merely that something threw.
/// </remarks>
public sealed class EffectOpSeamTests
{
    /// <summary>Every `18` §2.2 op names M2-09 rather than resolving to nothing.</summary>
    [Theory]
    [InlineData(EffectOp.DAMAGE)]
    [InlineData(EffectOp.DAMAGE_TRUE)]
    [InlineData(EffectOp.DAMAGE_MAXHP_PCT)]
    [InlineData(EffectOp.HEAL)]
    [InlineData(EffectOp.SHIELD)]
    [InlineData(EffectOp.REFLECT)]
    public void An_unwired_damage_or_healing_op_names_M2_09(EffectOp op)
    {
        var thrown = Should.Throw<EffectContextException>(() => Resolve(op));

        thrown.Message.ShouldContain("M2-09", Case.Sensitive);
        thrown.Message.ShouldContain(EffectContextException.Marker, Case.Sensitive);
    }

    /// <summary>Every `18` §2.3 op names M2-10.</summary>
    [Theory]
    [InlineData(EffectOp.APPLY_STATUS)]
    [InlineData(EffectOp.REMOVE_STATUS)]
    [InlineData(EffectOp.EXTEND_STATUS)]
    [InlineData(EffectOp.IMMUNE_STATUS)]
    [InlineData(EffectOp.STATUS_POWER_PCT)]
    [InlineData(EffectOp.STATUS_DURATION_PCT)]
    public void An_unwired_status_op_names_M2_10(EffectOp op) =>
        Should.Throw<EffectContextException>(() => Resolve(op))
              .Message.ShouldContain("M2-10", Case.Sensitive);

    /// <summary>Every `18` §2.4 flow op names M2-08.</summary>
    [Theory]
    [InlineData(EffectOp.EXTRA_ATTACK)]
    [InlineData(EffectOp.ATTACK_MULT_NEXT)]
    [InlineData(EffectOp.FORCE_CRIT_NEXT)]
    [InlineData(EffectOp.REDUCE_COOLDOWN)]
    [InlineData(EffectOp.SURVIVE_LETHAL)]
    [InlineData(EffectOp.REVIVE)]
    [InlineData(EffectOp.SUMMON)]
    [InlineData(EffectOp.SET_TARGET_PRIORITY)]
    [InlineData(EffectOp.DAMAGE_TAKEN_MULT)]
    [InlineData(EffectOp.CLEAR_SUMMONS)]
    public void An_unwired_combat_flow_op_names_M2_08(EffectOp op) =>
        Should.Throw<EffectContextException>(() => Resolve(op))
              .Message.ShouldContain("M2-08", Case.Sensitive);

    /// <summary>`18` §1.1's <c>valueScale</c> still names M2-06 — M2-03 owns only the value mode.</summary>
    [Fact]
    public void A_valueScale_names_M2_06_and_not_M2_03()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var scaled = OpFixtures.Effect("PK_HOARD", EffectOp.DAMAGE_TRUE, 0.01, EffectTarget.SELF) with
        {
            ValueMode = ValueMode.FLAT,
            ValueScale = new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = 100 },
        };

        Should.Throw<EffectContextException>(
                  () => EffectOpResolver.Resolve(scaled, bench.Context(EffectTestBattle.Context(hero, hero))))
              .Message.ShouldContain("M2-06", Case.Sensitive);
    }

    /// <summary>An effect with no value is refused rather than read as zero (steering S6).</summary>
    [Fact]
    public void An_effect_with_no_value_is_refused_rather_than_read_as_zero()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var valueless = new EffectDefinition
        {
            Id = "PK_NO_VALUE", Op = EffectOp.DAMAGE_TRUE, Target = EffectTarget.SELF,
            ValueMode = ValueMode.FLAT,
        };

        Should.Throw<EffectContextException>(
                  () => EffectOpResolver.Resolve(valueless, bench.Context(EffectTestBattle.Context(hero, hero))))
              .Token.ShouldBe("PK_NO_VALUE");
    }

    /// <summary>
    /// 🔒 An op that needs a target and authors none is refused, naming M2-02 as the owner of the
    /// default question — never silently pointed at <c>SELF</c> or the current target.
    /// </summary>
    [Fact]
    public void An_op_with_no_authored_target_names_the_unresolved_default_rather_than_guessing()
    {
        var hero = EffectTestBattle.Hero();
        var bench = new OpTestBench();

        var untargeted = OpFixtures.Effect("PK_X", EffectOp.HEAL, 10.0) with { ValueMode = ValueMode.FLAT };

        var thrown = Should.Throw<EffectContextException>(
            () => EffectOpResolver.Resolve(untargeted, bench.Context(EffectTestBattle.Context(hero, hero))));

        thrown.Message.ShouldContain("M2-02", Case.Sensitive);
        bench.Calls.ShouldBeEmpty();
    }

    private static void Resolve(EffectOp op)
    {
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("EN_1", 1);

        var effect = new EffectDefinition
        {
            Id = $"EX_{op}",
            Op = op,
            Value = op == EffectOp.FORCE_CRIT_NEXT ? null : 1.0,
            Target = EffectTarget.CURRENT_TARGET,
            // FLAT keeps the basis out of the assertion for the ops that admit it. The four that do
            // not each take their own default — DAMAGE's value IS the multiplier, HEAL_LEECH reads
            // the damage basis, DAMAGE_MAXHP_PCT and REVIVE are Max-HP fractions — so those are left
            // unset rather than forced into a mode their own 18 §2 row rules out.
            ValueMode = op is EffectOp.DAMAGE or EffectOp.HEAL_LEECH
                             or EffectOp.DAMAGE_MAXHP_PCT or EffectOp.REVIVE
                ? null
                : ValueMode.FLAT,
            StatusId = "BURN",
            Charges = 1,
            Archetype = "SWARM",
        };

        var evaluation = EffectTestBattle.Context(hero, hero, enemy) with { CurrentTarget = enemy };

        EffectOpResolver.Resolve(
            effect,
            new EffectOpContext
            {
                Evaluation = evaluation,
                Seams = EffectOpSeams.Strict,
                DamageDealt = 100.0,
            });
    }
}
