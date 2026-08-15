using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content.Effects;

/// <summary>
/// <c>effectiveValue = value × steps</c>, <c>steps = min( floor( fn / per ), cap )</c>,
/// with <c>fn</c> rounded to 4 decimal places <b>before</b> the division.
/// </summary>
public sealed class ValueScaleTests
{
    /// <summary>PK_BERSERK Tier I: "+1% ATK per 1% missing HP, up to +45%".</summary>
    [Theory]
    [InlineData(0.00, 0, 0.00)]
    [InlineData(0.01, 1, 0.01)]
    [InlineData(0.30, 30, 0.30)]
    [InlineData(0.45, 45, 0.45)]
    [InlineData(0.90, 45, 0.45)]   // the cap bites
    [InlineData(1.00, 45, 0.45)]
    public void PK_BERSERK_scales_one_percent_of_ATK_per_percent_of_missing_HP_up_to_45(
        double missingHpFraction, int expectedSteps, double expectedValue)
    {
        var scale = new ValueScale { Fn = ConditionFunction.SELF_MISSING_HP_PCT, Per = 0.01, Cap = 45 };

        scale.StepsFor(missingHpFraction).ShouldBe(expectedSteps);
        scale.EffectiveValue(0.01, missingHpFraction).ShouldBe(expectedValue, 1e-9);
    }

    /// <summary>PK_HOARD: "+1% ATK per 100 Gold currently held", uncapped.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(99, 0)]
    [InlineData(100, 1)]
    [InlineData(1_000, 10)]
    [InlineData(1_000_000, 10_000)]
    public void PK_HOARD_scales_one_percent_of_ATK_per_hundred_gold_uncapped(double goldHeld, int expectedSteps)
    {
        var scale = new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = 100, Cap = null };

        scale.StepsFor(goldHeld).ShouldBe(expectedSteps);
    }

    /// <summary>
    /// 🔒 The rounding is on the READING, before the division — the case that tells the two orders
    /// apart.
    /// </summary>
    /// <remarks>
    /// <c>0.449994</c> is <c>0.4500</c> to four places, so rounding first gives <c>0.45 / 0.01 = 45</c>
    /// steps. Both plausible alternatives give <b>44</b>: dividing the raw value floors
    /// <c>44.9994</c>, and rounding the quotient leaves it untouched.
    /// </remarks>
    [Fact]
    public void The_reading_is_rounded_to_four_places_before_the_division_not_after_and_not_never()
    {
        var scale = new ValueScale { Fn = ConditionFunction.SELF_MISSING_HP_PCT, Per = 0.01, Cap = null };

        // A reading that is 0.4500 to four places but strictly below 0.45.
        const double JustUnder = 0.449994;

        JustUnder.ShouldBeLessThan(0.45);
        Math.Round(JustUnder, 4).ShouldBe(0.45);

        Math.Floor(JustUnder / 0.01).ShouldBe(44, "no rounding at all floors one step lower");
        Math.Floor(Math.Round(JustUnder / 0.01, 4)).ShouldBe(44, "rounding the QUOTIENT changes nothing here");

        scale.StepsFor(JustUnder).ShouldBe(
            45,
            "18 §1.1 rounds fn to 4 dp BEFORE the division; both alternatives give 44");
    }

    /// <summary>The cap clamps from above and never from below: <c>min(…, cap)</c>.</summary>
    [Fact]
    public void The_cap_is_a_maximum_not_a_target()
    {
        var scale = new ValueScale { Fn = ConditionFunction.PET_COUNT, Per = 1, Cap = 3 };

        scale.StepsFor(0).ShouldBe(0);
        scale.StepsFor(2).ShouldBe(2);
        scale.StepsFor(3).ShouldBe(3);
        scale.StepsFor(9).ShouldBe(3);
    }

    [Fact]
    public void A_cap_of_zero_pins_the_effect_to_nothing_rather_than_being_read_as_absent()
    {
        var scale = new ValueScale { Fn = ConditionFunction.PET_COUNT, Per = 1, Cap = 0 };

        scale.StepsFor(9).ShouldBe(0);
        scale.EffectiveValue(0.5, 9).ShouldBe(0);
    }

    /// <summary>
    /// 🔒 A negative <c>value</c> scaled by <b>zero steps</b> must produce <c>+0.0</c>, never
    /// <c>-0.0</c>.
    /// </summary>
    /// <remarks>
    /// <c>CanonicalStateWriter</c> throws on a negative zero rather than encoding one, since the two are
    /// different bit patterns and one state would hash two ways;
    /// <see cref="ValueScale.EffectiveValue"/> is the accumulation point where it is normalised.
    /// <para>
    /// ⚠️ The assertion has to be <c>double.IsNegative</c>: <c>(-0.0).Equals(0.0)</c> is <b>true</b>, so
    /// <c>ShouldBe(0)</c> cannot see the sign — which is why the defect survived the first round.
    /// Reachable with real numbers: Bog Air is <c>-0.35</c>, and zero steps is the ordinary
    /// reading at full HP, at zero gold and under a cap of zero.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData(-0.35, 0.0)]
    [InlineData(-1.0, 0.0)]
    [InlineData(-0.0001, 0.0)]
    public void A_negative_value_over_zero_steps_produces_positive_zero(double value, double reading)
    {
        var scale = new ValueScale { Fn = ConditionFunction.SELF_MISSING_HP_PCT, Per = 0.01, Cap = 45 };

        // The premise: the naive product really is a negative zero, so this case is testing the
        // normalisation and not an arithmetic accident.
        scale.StepsFor(reading).ShouldBe(0);
        double.IsNegative(Math.Round(value * 0, 4)).ShouldBeTrue(
            "value x 0 is -0.0 for a negative value, and Math.Round preserves the sign");

        var effective = scale.EffectiveValue(value, reading);

        effective.ShouldBe(0);
        double.IsNegative(effective).ShouldBeFalse(
            "CanonicalStateWriter throws on -0.0; 18 §8 step 10 makes this the accumulation point " +
            "that has to normalise it");
    }

    /// <summary>
    /// 🔒 <c>cap</c> is a maximum. Negative is not "no cap" — <c>null</c> is — and a negative cap
    /// would clamp every reading to a negative step count and invert the effect.
    /// <c>game-data/schema/effect.schema.json</c> declares <c>"minimum": 0</c>; this is the same
    /// bound on the C# side, so a scale built in code cannot reach a state authored JSON cannot.
    /// </summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-45)]
    public void A_negative_cap_is_rejected_at_construction(int cap)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = 100, Cap = cap });

        thrown.ParamName.ShouldBe("Cap");
        thrown.Message.ShouldContain("inverts", Case.Sensitive);
    }

    /// <summary>
    /// <c>min(…, cap)</c> clamps from above only, so a negative reading yields negative steps. No
    /// lower clamp is imposed — a bound the design has not authorised is not one to invent — and
    /// this pins the behaviour so that adding one later is a deliberate change rather than a silent one.
    /// </summary>
    [Fact]
    public void A_negative_reading_yields_negative_steps_because_18_states_no_lower_bound()
    {
        var scale = new ValueScale { Fn = ConditionFunction.SELF_MISSING_HP_PCT, Per = 0.01, Cap = 45 };

        scale.StepsFor(-0.5).ShouldBe(-50);
        scale.EffectiveValue(0.01, -0.5).ShouldBe(-0.5, 1e-9);
    }

    /// <summary>
    /// 🔒 The cap is applied <b>before</b> the int-range check, not after. A capped scale over an
    /// enormous reading is well defined — <c>min(…, cap)</c> is the cap — and swapping the two
    /// blocks would turn <c>PK_BERSERK</c> into a runtime throw at a reading it is designed to
    /// survive.
    /// </summary>
    [Fact]
    public void A_capped_scale_over_an_enormous_reading_returns_the_cap_rather_than_throwing()
    {
        var scale = new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = 0.0001, Cap = 45 };

        // Uncapped, this reading overflows an int — the sibling case below proves it.
        scale.StepsFor(1e15).ShouldBe(45);
    }

    /// <summary>
    /// The other half of the range guard. A reading far below zero cannot be rescued by a cap, and
    /// the message must not tell the author to add one.
    /// </summary>
    [Fact]
    public void A_reading_far_below_zero_fails_loudly_and_does_not_advise_a_cap()
    {
        var scale = new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = 0.0001, Cap = 45 };

        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => scale.StepsFor(-1e15));

        thrown.Message.ShouldContain("bounds this from above only", Case.Sensitive);
    }

    /// <summary>
    /// 🔒 <c>per</c> is the divisor. Zero is a division by zero and a negative one reverses the
    /// direction of every step, so neither is accepted and interpreted later.
    /// </summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(-0.01)]
    public void A_per_of_zero_or_less_is_rejected_at_construction(double per)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = per });

        thrown.Message.ShouldContain("18 §1.1", Case.Sensitive);
        thrown.ParamName.ShouldBe("Per");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_reading_that_is_not_a_finite_number_is_rejected_rather_than_floored(double reading)
    {
        var scale = new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = 100 };

        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => scale.StepsFor(reading));

        thrown.Message.ShouldContain("GOLD_HELD", Case.Sensitive);
    }

    [Fact]
    public void An_uncapped_scale_over_an_unbounded_reading_fails_loudly_rather_than_overflowing()
    {
        var scale = new ValueScale { Fn = ConditionFunction.GOLD_HELD, Per = 0.0001, Cap = null };

        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => scale.StepsFor(1e15));

        thrown.Message.ShouldContain("not a step count", Case.Sensitive);
        thrown.Message.ShouldContain("GOLD_HELD", Case.Sensitive);
    }

    /// <summary>The product is rounded to four places too — an <c>effectiveValue</c> feeds straight into an accumulation.</summary>
    [Fact]
    public void The_effective_value_is_rounded_to_four_places()
    {
        var scale = new ValueScale { Fn = ConditionFunction.PET_COUNT, Per = 1, Cap = null };

        // 3 × 0.123456789 = 0.370370367, which is not a 4-dp number.
        var raw = 3 * 0.123456789;
        Math.Round(raw, 4).ShouldNotBe(raw);

        scale.EffectiveValue(0.123456789, 3).ShouldBe(0.3704);
    }

    /// <summary>
    /// <c>valueScale: null</c> (the default) means <c>effectiveValue = value</c>: absence is modelled
    /// as a null property, not as a scale of one step.
    /// </summary>
    [Fact]
    public void An_effect_with_no_value_scale_carries_null_rather_than_an_identity_scale()
    {
        var effect = new EffectDefinition
        {
            Id = "PK_SHARP_EDGE_T1",
            Op = EffectOp.STAT_ADD_PCT,
            Stat = StatSelector.Of(StatId.ATK),
            Value = 0.12,
        };

        effect.ValueScale.ShouldBeNull();
    }

    /// <summary>
    /// Any of the 23 condition functions may drive a scale, stated over the whole enum so that
    /// shrinking it cannot quietly shrink this.
    /// </summary>
    [Fact]
    public void Every_condition_function_can_drive_a_value_scale()
    {
        var functions = Enum.GetValues<ConditionFunction>();

        functions.Length.ShouldBe(23);

        foreach (var fn in functions)
        {
            var scale = new ValueScale { Fn = fn, Per = 1, Cap = null };
            scale.Fn.ShouldBe(fn);
            scale.StepsFor(2).ShouldBe(2);
        }
    }
}
