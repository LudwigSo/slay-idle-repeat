using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Effects;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>
/// <see cref="IEffectValueReader"/>'s strict default refuses a <c>valueScale</c>; this is the
/// wiring that fills that seam where aggregation actually reads values from.
/// </summary>
public sealed class ScaledEffectValueTests
{
    /// <summary>
    /// <c>PK_BERSERK</c> through the whole pipeline: a 300 ATK hero at 60% HP gets +40% ATK, which
    /// is 420.
    /// </summary>
    [Fact]
    public void PK_BERSERK_reaches_the_aggregation_as_a_scaled_percent()
    {
        var berserking = Aggregate(
            atHpFraction: 0.60,
            new EffectDefinition
            {
                Id = "PK_BERSERK_I",
                Op = EffectOp.STAT_ADD_PCT,
                Stat = StatSelector.Of(StatId.ATK),
                Value = 0.01,
                Trigger = new EffectTrigger { Kind = TriggerKind.ALWAYS },
                Target = EffectTarget.SELF,
                ValueScale = new ValueScale
                {
                    Fn = ConditionFunction.SELF_MISSING_HP_PCT, Per = 0.01, Cap = 45,
                },
            });

        berserking.Final[StatId.ATK].ShouldBe(
            420.0, "40% missing HP is 40 steps of +1%, so 300 x 1.40");
    }

    /// <summary>
    /// 🔒 The cap reaches the aggregation too — <c>PK_BERSERK</c> is <em>"up to +45%"</em>, not
    /// "+1% per 1% missing HP" without end.
    /// </summary>
    [Fact]
    public void The_valueScale_cap_reaches_the_aggregation()
    {
        var effect = new EffectDefinition
        {
            Id = "PK_BERSERK_I",
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = 0.01,
            Trigger = new EffectTrigger { Kind = TriggerKind.ALWAYS },
            Target = EffectTarget.SELF,
            ValueScale = new ValueScale { Fn = ConditionFunction.SELF_MISSING_HP_PCT, Per = 0.01, Cap = 45 },
        };

        Aggregate(atHpFraction: 0.01, effect).Final[StatId.ATK].ShouldBe(
            435.0, "99% missing HP clamps to 45 steps, so 300 x 1.45");

        Aggregate(atHpFraction: 0.01, effect).Final[StatId.ATK].ShouldNotBe(
            597.0, "597 is 300 x 1.99 — the uncapped reading, and a perk 18 §1.1 does not author");
    }

    /// <summary>
    /// An unscaled effect still reads its authored value — the reader is a superset of
    /// <c>AuthoredEffectValue</c>, not a replacement with different behaviour.
    /// </summary>
    [Fact]
    public void An_unscaled_effect_still_reads_its_authored_value()
    {
        var sharpEdge = StatFixtures.Effect("PK_SHARP_EDGE_I", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.12);

        Aggregate(atHpFraction: 1.0, sharpEdge).Final[StatId.ATK].ShouldBe(336.0, "300 x 1.12");
    }

    /// <summary>
    /// The refusal of a non-<c>FLAT</c> <c>valueMode</c> on a stat op is kept: the only stat op
    /// that carries a mode is <c>STAT_SET MAX_HP</c> with <c>FLAT</c>, and what the other seven
    /// would mean against a stat is written nowhere.
    /// </summary>
    [Fact]
    public void A_non_FLAT_value_mode_on_a_stat_op_is_still_refused()
    {
        var wrong = StatFixtures.Effect("PK_WRONG_MODE", EffectOp.STAT_ADD_PCT, StatId.ATK, 0.12)
            with { ValueMode = ValueMode.TARGET_MAXHP_PCT };

        var thrown = Should.Throw<EffectContextException>(() => Aggregate(atHpFraction: 1.0, wrong));

        thrown.Message.ShouldContain("PK_WRONG_MODE", Case.Sensitive);
        thrown.Message.ShouldContain("18 §2.2");
    }

    // ───────────────────────────────────────────── fixtures

    private static AggregatedStats Aggregate(double atHpFraction, params EffectDefinition[] effects)
    {
        var hero = EffectTestBattle.Hero(currentHp: atHpFraction * 100.0, maxHp: 100.0);
        var context = EffectTestBattle.Context(hero, hero);

        var baseStats = ActorStats.From(
            StatIds.Combat.ToDictionary(stat => stat, stat => stat == StatId.ATK ? 300.0 : 0.0));

        var seams = StatAggregationSeams.Strict with { Values = new ScaledEffectValue(context) };

        return StatAggregation.Aggregate(baseStats, effects, StatCaps.None, seams);
    }
}
