using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Ops;

/// <summary>Every unwired seam throws naming the task that owns it, rather than quietly doing nothing.</summary>
/// <remarks>
/// A no-op default would turn "the seam hasn't landed" into "this perk does nothing", which nothing
/// else would catch — so each case asserts the owner named in the message, not merely that it threw.
/// </remarks>
public sealed class EffectOpSeamTests
{
    /// <summary>Every damage/healing op names <b>M2-09</b> rather than resolving to nothing.</summary>
    /// <remarks>
    /// The families are enumerated via the classifier, not hand-listed — a hand-written list can
    /// silently lose a member and still claim "every op".
    /// </remarks>
    [Theory]
    [MemberData(nameof(DamageAndHealingOps))]
    public void An_unwired_damage_or_healing_op_names_M2_09(EffectOp op) => Unwired(op, "M2-09");

    /// <summary>Every status op names <b>M2-10</b>.</summary>
    [Theory]
    [MemberData(nameof(StatusOps))]
    public void An_unwired_status_op_names_M2_10(EffectOp op) => Unwired(op, "M2-10");

    /// <summary>
    /// Every combat-flow op names <b>M2-08</b> — including <c>STAT_COPY</c>, whose unwired path is
    /// the stat-snapshot reader rather than the flow sink.
    /// </summary>
    [Theory]
    [MemberData(nameof(CombatFlowOps))]
    public void An_unwired_combat_flow_op_names_M2_08(EffectOp op) => Unwired(op, "M2-08");

    /// <summary>Damage/healing family: seven ops, off the family classifier.</summary>
    public static TheoryData<EffectOp> DamageAndHealingOps() => Family(EffectOpFamily.DAMAGE_AND_HEALING, 7);

    /// <summary>Status family: six ops.</summary>
    public static TheoryData<EffectOp> StatusOps() => Family(EffectOpFamily.STATUS, 6);

    /// <summary>Combat-flow family: twelve ops, including <c>RANDOM_OUTCOME</c>.</summary>
    public static TheoryData<EffectOp> CombatFlowOps() => Family(EffectOpFamily.COMBAT_FLOW, 12);

    /// <summary>A fired basic stat op names <b>M2-R1</b> rather than resolving to nothing.</summary>
    [Theory]
    [MemberData(nameof(FiredStatOps))]
    public void An_unwired_fired_stat_op_names_M2_R1(EffectOp op) => Unwired(op, "M2-R1");

    /// <summary>The four basic stat ops that a trigger can fire — excludes STAT_CONVERT/STAT_CAP_OVERRIDE.</summary>
    public static TheoryData<EffectOp> FiredStatOps()
    {
        var data = new TheoryData<EffectOp>();
        foreach (var op in new[]
                 {
                     EffectOp.STAT_ADD_FLAT, EffectOp.STAT_ADD_PCT, EffectOp.STAT_MULT, EffectOp.STAT_SET,
                 })
        {
            data.Add(op);
        }

        return data;
    }

    /// <summary><c>valueScale</c> still names M2-06 — M2-03 owns only the value mode.</summary>
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

        // Every refusal rule — missing value, valueScale, missing target, unwired seam — carries the token too.
        thrown.Token.ShouldBe("PK_NO_VALUE");
        thrown.Message.ShouldContain("with no value", Case.Sensitive);
    }

    /// <summary>An op that authors no <c>target</c> resolves against the holder.</summary>
    [Fact]
    public void An_op_with_no_authored_target_resolves_against_the_holder()
    {
        var hero = EffectTestBattle.Hero(currentHp: 40);
        var bench = new OpTestBench();

        var untargeted = OpFixtures.Effect("PK_X", EffectOp.HEAL, 10.0) with { ValueMode = ValueMode.FLAT };

        EffectOpResolver.Resolve(untargeted, bench.Context(EffectTestBattle.Context(hero, hero)));

        // Confirms the heal landed on the HOLDER specifically: a CURRENT_TARGET default would have
        // thrown here (no target in context), and any enemy default would name someone else.
        bench.Only("Heal").Actor.ShouldBe("HERO");
    }

    /// <summary>An absent target resolves to <c>SELF</c>, matching the resolver's own default.</summary>
    [Fact]
    public void An_absent_target_is_SELF_per_18_2_4s_CLEAR_SUMMONS_row()
    {
        var surviveLethal = new EffectDefinition
        {
            Id = "PK_UNBREAKABLE", Op = EffectOp.SURVIVE_LETHAL, Value = 1, ValueMode = ValueMode.FLAT,
        };

        EffectDefaults.TargetOf(surviveLethal).ShouldBe(EffectTarget.SELF);

        // An authored target is never overridden.
        EffectDefaults.TargetOf(surviveLethal with { Target = EffectTarget.ALL_ENEMIES })
                      .ShouldBe(EffectTarget.ALL_ENEMIES);
    }

    /// <summary>
    /// Every family is read off <see cref="EffectOps.FamilyOf"/> with a floor, so a missing member
    /// turns the count red first.
    /// </summary>
    /// <param name="family">The op family.</param>
    /// <param name="expected">Its full size — the floor, asserted before anything is excluded.</param>
    /// <param name="except">An op to exclude, named rather than filtered silently.</param>
    private static TheoryData<EffectOp> Family(EffectOpFamily family, int expected, EffectOp? except = null)
    {
        var ops = EffectOps.All.Where(op => EffectOps.FamilyOf(op) == family).ToArray();

        ops.Length.ShouldBe(expected, $"18 §2 tabulates {expected} ops in {family}");

        var data = new TheoryData<EffectOp>();
        foreach (var op in ops.Where(op => op != except))
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

        // The same exemplar table the resolver and validation suites use, so all three agree on
        // what an authorable effect of a given op looks like.
        var effect = OpFixtures.Exemplar(op);

        // FLAT keeps the basis out of the assertion for ops that admit it. The rest take their own
        // default — DAMAGE's value IS the multiplier, HEAL_LEECH reads the damage basis,
        // DAMAGE_MAXHP_PCT/REVIVE are Max-HP fractions, RANDOM_OUTCOME carries no value at all — so
        // those are left unset rather than forced into a mode their own op rules out.
        if (op is not (EffectOp.DAMAGE or EffectOp.HEAL_LEECH
                       or EffectOp.DAMAGE_MAXHP_PCT or EffectOp.REVIVE or EffectOp.FORCE_CRIT_NEXT
                       or EffectOp.RANDOM_OUTCOME))
        {
            effect = effect with { ValueMode = ValueMode.FLAT };
        }

        var evaluation = EffectTestBattle.Context(hero, hero, enemy) with
        {
            CurrentTarget = enemy,
            Attacker = enemy,

            // RANDOM_OUTCOME draws before naming its winner; without a stream it would be refused for
            // the missing stream instead of reaching the seam this theory is about.
            Rng = EffectTestBattle.CombatRng(6),
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
