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
    /// <summary>
    /// Every `18` §2.2 op names <b>M2-09</b> rather than resolving to nothing.
    /// </summary>
    /// <remarks>
    /// 🔒 The families are <b>enumerated</b>, not hand-listed. A hand-written <c>InlineData</c> list
    /// is a subject set that silently loses a member: this suite shipped one short of §2.2 and one
    /// short of §2.4 until the review counted them, so <c>HEAL_LEECH</c>'s and <c>STAT_COPY</c>'s
    /// unwired paths were untested while the summary claimed "every op".
    /// </remarks>
    [Theory]
    [MemberData(nameof(DamageAndHealingOps))]
    public void An_unwired_damage_or_healing_op_names_M2_09(EffectOp op) => Unwired(op, "M2-09");

    /// <summary>Every `18` §2.3 op names <b>M2-10</b>.</summary>
    [Theory]
    [MemberData(nameof(StatusOps))]
    public void An_unwired_status_op_names_M2_10(EffectOp op) => Unwired(op, "M2-10");

    /// <summary>
    /// Every `18` §2.4 op names <b>M2-08</b> — including <c>STAT_COPY</c>, whose unwired path is the
    /// stat-snapshot reader rather than the flow sink.
    /// </summary>
    [Theory]
    [MemberData(nameof(CombatFlowOps))]
    public void An_unwired_combat_flow_op_names_M2_08(EffectOp op) => Unwired(op, "M2-08");

    /// <summary>`18` §2.2's seven, off the family classifier.</summary>
    public static TheoryData<EffectOp> DamageAndHealingOps() => Family(EffectOpFamily.DAMAGE_AND_HEALING, 7);

    /// <summary>`18` §2.3's six.</summary>
    public static TheoryData<EffectOp> StatusOps() => Family(EffectOpFamily.STATUS, 6);

    /// <summary>`18` §2.4's eleven.</summary>
    public static TheoryData<EffectOp> CombatFlowOps() => Family(EffectOpFamily.COMBAT_FLOW, 11);

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

        var thrown = Should.Throw<EffectContextException>(
            () => EffectOpResolver.Resolve(scaled, bench.Context(EffectTestBattle.Context(hero, hero))));

        thrown.Message.ShouldContain("M2-06", Case.Sensitive);
        thrown.Message.ShouldNotContain("M2-03", Case.Sensitive, "the name in the title is half the claim");
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

        var thrown = Should.Throw<EffectContextException>(
            () => EffectOpResolver.Resolve(valueless, bench.Context(EffectTestBattle.Context(hero, hero))));

        // S2 — the token alone is not the identity: the missing-value rule, the valueScale rule, the
        // missing-target rule and every unwired seam all carry it.
        thrown.Token.ShouldBe("PK_NO_VALUE");
        thrown.Message.ShouldContain("with no value", Case.Sensitive);
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

    /// <summary>
    /// 🔒 S3 — every family is read off <see cref="EffectOps.FamilyOf"/> with a floor, so a member
    /// cannot go missing from a theory without the count going red first.
    /// </summary>
    private static TheoryData<EffectOp> Family(EffectOpFamily family, int expected)
    {
        var ops = EffectOps.All.Where(op => EffectOps.FamilyOf(op) == family).ToArray();

        ops.Length.ShouldBe(expected, $"18 §2 tabulates {expected} ops in {family}");

        var data = new TheoryData<EffectOp>();
        foreach (var op in ops)
        {
            data.Add(op);
        }

        return data;
    }

    /// <summary>Resolves one op against the strict seams and asserts the owner it names.</summary>
    private static void Unwired(EffectOp op, string owner)
    {
        var thrown = Should.Throw<EffectContextException>(() => Resolve(op));

        thrown.Message.ShouldContain(owner, Case.Sensitive);
        thrown.Message.ShouldStartWith(EffectContextException.Marker, Case.Sensitive);
    }

    private static void Resolve(EffectOp op)
    {
        var hero = EffectTestBattle.Hero();
        var enemy = EffectTestBattle.Enemy("EN_1", 1);

        // 🔒 The SAME exemplar table the resolver and validation suites use, so the three cannot
        //    disagree about what an authorable effect of a given op looks like — and so no exemplar
        //    here carries a key the schema would reject.
        var effect = OpFixtures.Exemplar(op);

        // FLAT keeps the basis out of the assertion for the ops that admit it. The four that do not
        // each take their own default — DAMAGE's value IS the multiplier, HEAL_LEECH reads the damage
        // basis, DAMAGE_MAXHP_PCT and REVIVE are Max-HP fractions — so those are left unset rather
        // than forced into a mode their own 18 §2 row rules out.
        if (op is not (EffectOp.DAMAGE or EffectOp.HEAL_LEECH
                       or EffectOp.DAMAGE_MAXHP_PCT or EffectOp.REVIVE or EffectOp.FORCE_CRIT_NEXT))
        {
            effect = effect with { ValueMode = ValueMode.FLAT };
        }

        var evaluation = EffectTestBattle.Context(hero, hero, enemy) with
        {
            CurrentTarget = enemy,
            Attacker = enemy,
        };

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
