using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Stats;

/// <summary>The 4-decimal-place rounding at every accumulation point.</summary>
public sealed class StatRoundingTests
{
    [Fact]
    public void The_locked_precision_is_four_decimal_places()
    {
        StatRounding.Decimals.ShouldBe(4);
    }

    [Fact]
    public void A_value_is_rounded_to_four_places()
    {
        StatRounding.Round(1.234_56, StatId.ATK, "a test").ShouldBe(1.2346);
        StatRounding.Round(125.971_200_000_000_02, StatId.ATK, "a test").ShouldBe(125.9712);
    }

    /// <summary>
    /// 🔒 The <c>+ 0.0</c>: <c>-0.0 == 0.0</c> in C# but the bit patterns differ, so two
    /// "identical" states could hash differently. This is the accumulation point where that gets
    /// normalised.
    /// </summary>
    [Fact]
    public void A_negative_zero_is_normalised_rather_than_carried()
    {
        double.IsNegative(Math.Round(-0.000_04, 4)).ShouldBeTrue(
            "Math.Round preserves the sign of zero — which is what makes the normalisation necessary");

        var rounded = StatRounding.Round(-0.000_04, StatId.DEF, "a test");

        rounded.ShouldBe(0.0);
        double.IsNegative(rounded).ShouldBeFalse();
        BitConverter.DoubleToInt64Bits(rounded).ShouldBe(0L);
    }

    [Fact]
    public void An_already_rounded_value_is_unchanged()
    {
        StatRounding.Round(0.75, StatId.CRIT, "a test").ShouldBe(0.75);
        StatRounding.Round(2950.0, StatId.MAX_HP, "a test").ShouldBe(2950.0);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void A_value_that_is_not_finite_fails_at_the_step_that_produced_it(double value)
    {
        var thrown = Should.Throw<ArithmeticException>(
            () => StatRounding.Round(value, StatId.MAX_HP, "step 7 (STAT_MULT)"));

        thrown.Message.ShouldContain("step 7 (STAT_MULT)", Case.Sensitive);
        thrown.Message.ShouldContain("MAX_HP", Case.Sensitive);
        thrown.Message.ShouldContain("CanonicalStateWriter", Case.Sensitive);
    }

    [Fact]
    public void IsRounded_accepts_exactly_what_Round_produces()
    {
        StatRounding.IsRounded(1.2346).ShouldBeTrue();
        StatRounding.IsRounded(0.0).ShouldBeTrue();

        StatRounding.IsRounded(1.234_56).ShouldBeFalse("five decimal places");
        StatRounding.IsRounded(-0.0).ShouldBeFalse("a negative zero is not what Round produces");
        StatRounding.IsRounded(double.NaN).ShouldBeFalse();
        StatRounding.IsRounded(double.PositiveInfinity).ShouldBeFalse();
    }
}
