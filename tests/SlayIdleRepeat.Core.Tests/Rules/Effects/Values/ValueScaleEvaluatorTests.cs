using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Values;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Values;

/// <summary>
/// <c>effectiveValue = value × steps</c>, <c>steps = min( floor( fn / per ), cap )</c>, with the
/// reading rounded to 4 dp before the division.
/// </summary>
/// <remarks>
/// The arithmetic itself is <see cref="ValueScale"/>'s and is already pinned by
/// <c>ValueScaleTests</c>. What is pinned here is the wiring: that the reading comes from
/// <c>ConditionEvaluator.Read</c> and reaches <see cref="ValueScale.StepsFor"/> unmodified, and that
/// the two worked examples evaluate to the numbers the document captions.
/// </remarks>
public sealed class ValueScaleEvaluatorTests
{
    // ───────────────────────────────────────────── the two worked examples

    /// <summary><c>PK_BERSERK</c> Tier I — "+1% ATK per 1% missing HP, up to +45%".</summary>
    [Fact]
    public void PK_BERSERK_scales_one_percent_of_ATK_per_one_percent_of_missing_HP()
    {
        var berserk = Berserk();

        // 40% missing HP -> floor(0.40 / 0.01) = 40 steps -> 40 x 0.01 = +0.40.
        ValueScaleEvaluator.EffectiveValue(berserk, AtHp(0.60)).ShouldBe(0.40);

        // Full health -> zero steps -> +0.00, and NOT a negative zero (CanonicalStateWriter throws).
        var atFullHealth = ValueScaleEvaluator.EffectiveValue(berserk, AtHp(1.00));
        atFullHealth.ShouldBe(0.0);
        double.IsNegative(atFullHealth).ShouldBeFalse(
            "18 §8 step 10 is an accumulation point and CanonicalStateWriter refuses -0.0");
    }

    /// <summary>
    /// The cap is <c>min(…, cap)</c> — <c>PK_BERSERK</c> stops at +45%, which is the whole difference
    /// between the perk the document authors and one that reaches +99% at 1 HP.
    /// </summary>
    [Fact]
    public void PK_BERSERK_stops_at_its_45_step_cap()
    {
        var berserk = Berserk();

        ValueScaleEvaluator.EffectiveValue(berserk, AtHp(0.55)).ShouldBe(
            0.45, "45% missing HP is exactly the cap");

        ValueScaleEvaluator.EffectiveValue(berserk, AtHp(0.01)).ShouldBe(
            0.45, "18 §1.1: steps = min(floor(fn / per), cap) — 99 steps clamp to 45");
    }

    /// <summary>
    /// <c>PK_HOARD</c> — "+1% ATK per 100 Gold currently held", uncapped. <c>GOLD_HELD</c> is a
    /// <see cref="long"/> on <c>IRunStateView</c> and readings cross to <see cref="double"/>, which is
    /// exact to ≈1.4 × 10¹³ — a precision limit under an uncapped scale, not a determinism break,
    /// since it is identical on every platform.
    /// </summary>
    [Fact]
    public void PK_HOARD_is_uncapped()
    {
        var hoard = Hoard();

        ValueScaleEvaluator.EffectiveValue(hoard, WithGold(250)).ShouldBe(
            0.02, "floor(250 / 100) = 2 steps");

        ValueScaleEvaluator.EffectiveValue(hoard, WithGold(99)).ShouldBe(
            0.0, "under one step's worth of gold is no bonus at all");

        ValueScaleEvaluator.EffectiveValue(hoard, WithGold(5_000_000_000L)).ShouldBe(
            500_000.0,
            "18 §1.1: cap null is uncapped, and GOLD_HELD is a long — a 32-bit balance would have wrapped");
    }

    // ───────────────────────────────────────────── the no-double-rounding contract

    /// <summary>
    /// The reading is rounded once, by <c>ConditionEvaluator.Read</c>, and handed on unmodified: the
    /// 4-dp rounding happens on the reading before the division, and rounding the quotient instead
    /// moves a step boundary.
    /// </summary>
    [Fact]
    public void The_reading_is_rounded_before_the_division_never_after()
    {
        var berserk = Berserk();

        // HP is a double: 100 - 55.000049 is 44.999951, so the raw missing fraction is 0.44999951 ->
        // Read rounds to 0.45 -> floor(0.45 / 0.01) = 45 steps -> +0.45.
        ValueScaleEvaluator.EffectiveValue(berserk, AtHp(0.55000049)).ShouldBe(
            0.45,
            "18 §1.1 rounds fn to 4 dp BEFORE the division: 0.44999951 rounds to 0.45, which is 45 steps");

        ValueScaleEvaluator.EffectiveValue(berserk, AtHp(0.55000049)).ShouldNotBe(
            0.44,
            "0.44 is floor(0.44999951 / 0.01) — the answer an evaluator that divided the RAW reading " +
            "would give, and a one-step client/server divergence at exactly the boundary 14 §8.2 exists " +
            "to remove");
    }

    /// <summary>
    /// The evaluator's step count is <see cref="ValueScale.StepsFor"/>'s, over
    /// <c>ConditionEvaluator.Read</c>'s output — not a second arithmetic.
    /// </summary>
    [Fact]
    public void Steps_are_ValueScales_own_arithmetic_over_the_evaluators_reading()
    {
        var scale = new ValueScale { Fn = ConditionFunction.SELF_MISSING_HP_PCT, Per = 0.01, Cap = 45 };

        ValueScaleEvaluator.Steps(scale, AtHp(0.60)).ShouldBe(scale.StepsFor(0.40));
        ValueScaleEvaluator.Steps(scale, AtHp(0.60)).ShouldBe(40);
    }

    // ───────────────────────────────────────────── the default, and the absent value

    /// <summary><c>valueScale: null</c> (the default) means <c>effectiveValue = value</c>.</summary>
    [Fact]
    public void An_effect_with_no_valueScale_keeps_its_authored_value()
    {
        var sharpEdge = new EffectDefinition
        {
            Id = "PK_SHARP_EDGE_I",
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = 0.12,
        };

        ValueScaleEvaluator.EffectiveValue(sharpEdge, AtHp(0.60)).ShouldBe(0.12);
    }

    /// <summary>
    /// An effect carrying a <c>valueScale</c> and no <c>value</c> is an authoring hole, and
    /// <c>0 × steps</c> is a silent no-op. It is refused, naming the effect (S2).
    /// </summary>
    [Fact]
    public void A_scaled_effect_with_no_value_is_refused_by_name()
    {
        var holed = Berserk() with { Value = null };

        var thrown = Should.Throw<EffectContextException>(
            () => ValueScaleEvaluator.EffectiveValue(holed, AtHp(0.60)));

        thrown.Message.ShouldContain("PK_BERSERK_I", Case.Sensitive);
        thrown.Message.ShouldContain("18 §1.1");
    }

    // ───────────────────────────────────────────── fixtures

    private static EffectDefinition Berserk() =>
        new()
        {
            Id = "PK_BERSERK_I",
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = 0.01,
            Trigger = new EffectTrigger { Kind = TriggerKind.ALWAYS },
            Target = EffectTarget.SELF,
            ValueScale = new ValueScale { Fn = ConditionFunction.SELF_MISSING_HP_PCT, Per = 0.01, Cap = 45 },
        };

    private static EffectDefinition Hoard() =>
        new()
        {
            Id = "PK_HOARD",
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = 0.01,
            Trigger = new EffectTrigger { Kind = TriggerKind.ALWAYS },
            Target = EffectTarget.SELF,
            ValueScale = new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = 100, Cap = null },
        };

    private static EffectEvaluationContext AtHp(double fraction)
    {
        var hero = EffectTestBattle.Hero(currentHp: fraction * 100.0, maxHp: 100.0);

        return EffectTestBattle.Context(hero, hero);
    }

    private static EffectEvaluationContext WithGold(long gold)
    {
        var hero = EffectTestBattle.Hero();

        return EffectTestBattle.Context(hero, hero) with
        {
            Run = EffectTestBattle.Run() with { GoldHeld = gold },
        };
    }
}
