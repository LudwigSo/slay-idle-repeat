using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>The 4-decimal-place rounding rule, pinned numerically at the one place it is stated.</summary>
/// <remarks>
/// Separate from <c>DeterminismRoundingRuleTests</c>, which is an IL scan asserting where the rule
/// is stated and nothing about what it computes. <c>CanonicalStateWriter</c> and <c>CombatLog</c>
/// guard with <c>DeterminismRounding.Round(value) != value</c>, which fires on a NaN only because
/// <c>NaN != NaN</c> — clamping a NaN to zero would make both guards go quiet with every other test
/// still green.
/// </remarks>
public sealed class DeterminismRoundingTests
{
    [Fact]
    public void The_rule_is_four_decimal_places()
    {
        DeterminismRounding.Decimals.ShouldBe(4);
    }

    /// <summary>
    /// NaN and both infinities pass through unchanged rather than being clamped, which is what
    /// makes the guard sites' <c>Round(v) != v</c> check fire on a NaN and wave an infinity through.
    /// </summary>
    [Fact]
    public void NaN_and_infinity_pass_through_unchanged()
    {
        double.IsNaN(DeterminismRounding.Round(double.NaN)).ShouldBeTrue();
        DeterminismRounding.Round(double.PositiveInfinity).ShouldBe(double.PositiveInfinity);
        DeterminismRounding.Round(double.NegativeInfinity).ShouldBe(double.NegativeInfinity);

        // The consequence the two guard sites are built on, asserted as the guard states it.
        (DeterminismRounding.Round(double.NaN) != double.NaN).ShouldBeTrue(
            "CanonicalStateWriter and CombatLog guard with `Round(v) != v`, and a NaN must fail it");
    }

    /// <summary>
    /// A value that rounds to a negative zero comes back as a positive one: <c>-0.0</c>'s bit
    /// pattern differs from <c>+0.0</c>'s while C# calls them equal, so one state would otherwise
    /// carry two different hashes.
    /// </summary>
    [Theory]
    [InlineData(-0.0)]
    [InlineData(-0.00004)]
    [InlineData(-0.000049999)]
    public void A_value_that_rounds_to_negative_zero_comes_back_positive(double value)
    {
        var rounded = DeterminismRounding.Round(value);

        rounded.ShouldBe(0.0);

        // `ShouldBe(0.0)` cannot see this: -0.0 == 0.0 is true in C#. The sign bit is the assertion.
        double.IsNegative(rounded).ShouldBeFalse(
            "Math.Round(-0.00004, 4) is -0.0 and .NET preserves the sign of zero; the `+ 0.0` is what " +
            "normalises it at the accumulation point rather than letting the writer edit state on its " +
            "way out");
    }

    /// <summary>
    /// Midpoints go to even (<c>Math.Round</c>'s default). The four rows below look like
    /// counterexamples but are not: a decimal literal ending in 5 usually is not a true midpoint —
    /// <c>0.00005</c> and <c>0.12345</c> have nearest doubles just below their decimal midpoint,
    /// <c>1.00005</c> and <c>0.12355</c> just above — so none is decided by the midpoint mode at all.
    /// </summary>
    [Theory]
    [InlineData(0.00005, 0.0)]      // nearest double is below the decimal midpoint
    [InlineData(1.00005, 1.0001)]   // nearest double is above it
    [InlineData(0.12345, 0.1234)]   // below
    [InlineData(0.12355, 0.1236)]   // above
    public void The_rounding_is_stated_and_stable(double value, double expected)
    {
        DeterminismRounding.Round(value).ShouldBe(expected);
    }

    /// <summary>
    /// Idempotent: rounding happens at every accumulation point, so a value that has already been
    /// rounded must survive being rounded again — otherwise each step would shave the number a
    /// little further.
    /// </summary>
    [Theory]
    [InlineData(1.0001)]
    [InlineData(-0.35)]
    [InlineData(117.0)]
    [InlineData(0.0)]
    public void Rounding_an_already_rounded_value_changes_nothing(double value)
    {
        var once = DeterminismRounding.Round(value);

        DeterminismRounding.Round(once).ShouldBe(once);
        DeterminismRounding.IsRounded(once).ShouldBeTrue();
    }

    /// <summary>
    /// <see cref="DeterminismRounding.IsRounded"/> is the predicate <c>Round</c>'s output satisfies
    /// and nothing else does — including three values that are not rounding failures but must never
    /// be waved through.
    /// </summary>
    [Fact]
    public void IsRounded_refuses_NaN_infinity_negative_zero_and_a_fifth_decimal()
    {
        DeterminismRounding.IsRounded(double.NaN).ShouldBeFalse(
            "Math.Round(NaN, 4) != NaN, and a NaN is exactly the value that must not be waved through");
        DeterminismRounding.IsRounded(double.PositiveInfinity).ShouldBeFalse();
        DeterminismRounding.IsRounded(double.NegativeInfinity).ShouldBeFalse();
        DeterminismRounding.IsRounded(-0.0).ShouldBeFalse(
            "a negative zero is what CanonicalStateWriter refuses to encode");

        DeterminismRounding.IsRounded(0.12345).ShouldBeFalse("a fifth decimal place is not rounded");

        DeterminismRounding.IsRounded(0.0).ShouldBeTrue();
        DeterminismRounding.IsRounded(0.1234).ShouldBeTrue();
        DeterminismRounding.IsRounded(-0.35).ShouldBeTrue();
    }
}
