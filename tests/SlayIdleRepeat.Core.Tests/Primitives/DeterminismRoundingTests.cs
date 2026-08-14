using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// 🔒 `05` §1.1's 4-decimal-place rule, pinned <b>numerically</b> at the one place it is stated.
/// </summary>
/// <remarks>
/// ⚠️ Separate from <c>DeterminismRoundingRuleTests</c>, which is an IL scan asserting <em>where</em>
/// the rule is stated and nothing about what it computes. Every other caller refuses NaN and infinity
/// before reaching the primitive, so their suites cannot see its behaviour on either — but
/// <c>CanonicalStateWriter</c> and <c>CombatLog</c> guard with
/// <c>DeterminismRounding.Round(value) != value</c>, which fires on a NaN only because
/// <c>NaN != NaN</c>. Make <c>Round</c> "helpfully" clamp a NaN to zero and both guards go quiet with
/// every other test still green.
/// </remarks>
public sealed class DeterminismRoundingTests
{
    /// <summary>🔒 The locked constant. `05` §1.1, `14` §8.2, `18` §8 step 10.</summary>
    [Fact]
    public void The_rule_is_four_decimal_places()
    {
        DeterminismRounding.Decimals.ShouldBe(4);
    }

    /// <summary>
    /// 🔒 <b>Total on NaN and both infinities</b> — they pass through unchanged rather than being
    /// clamped, which is what makes <c>CanonicalStateWriter</c>'s and <c>CombatLog</c>'s
    /// <c>Round(v) != v</c> guard fire on a NaN and wave an infinity through to their own arms.
    /// </summary>
    [Fact]
    public void NaN_and_infinity_pass_through_unchanged()
    {
        double.IsNaN(DeterminismRounding.Round(double.NaN)).ShouldBeTrue();
        DeterminismRounding.Round(double.PositiveInfinity).ShouldBe(double.PositiveInfinity);
        DeterminismRounding.Round(double.NegativeInfinity).ShouldBe(double.NegativeInfinity);

        // 🔒 The consequence the two guard sites are built on, asserted as the guard states it.
        (DeterminismRounding.Round(double.NaN) != double.NaN).ShouldBeTrue(
            "CanonicalStateWriter and CombatLog guard with `Round(v) != v`, and a NaN must fail it");
    }

    /// <summary>
    /// 🔒 <b>The trailing <c>+ 0.0</c>.</b> A value that rounds to a negative zero comes back as a
    /// positive one — <c>CanonicalStateWriter</c> throws rather than encoding a <c>-0.0</c>, because
    /// its bit pattern differs from <c>+0.0</c>'s while C# calls them equal, so one state would carry
    /// two <c>stateHash</c>es.
    /// </summary>
    [Theory]
    [InlineData(-0.0)]
    [InlineData(-0.00004)]
    [InlineData(-0.000049999)]
    public void A_value_that_rounds_to_negative_zero_comes_back_positive(double value)
    {
        var rounded = DeterminismRounding.Round(value);

        rounded.ShouldBe(0.0);

        // 🔒 `ShouldBe(0.0)` cannot see this: -0.0 == 0.0 is true in C#. The sign bit is the assertion.
        double.IsNegative(rounded).ShouldBeFalse(
            "Math.Round(-0.00004, 4) is -0.0 and .NET preserves the sign of zero; the `+ 0.0` is what " +
            "normalises it at the accumulation point rather than letting the writer edit state on its " +
            "way out");
    }

    /// <summary>
    /// 🔒 <b>Midpoints go to even</b> — <c>Math.Round</c>'s default, written out so the rule does not
    /// depend on six authors agreeing about a default.
    /// </summary>
    /// <remarks>
    /// ⚠️ A decimal literal ending in 5 is usually <b>not</b> a midpoint, and the four rows that look
    /// like counterexamples are the point: <c>0.00005</c> and <c>0.12345</c> have nearest doubles just
    /// <em>below</em> their decimal midpoint, <c>1.00005</c> and <c>0.12355</c> just <em>above</em>.
    /// None is decided by the midpoint mode — they are decided by binary representation, identically
    /// on every IEEE-754 platform, which is what makes this a determinism rule rather than a
    /// preference.
    /// </remarks>
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
    /// 🔒 <b>Idempotent.</b> `05` §1.1 rounds at <em>every</em> accumulation point, so a value that
    /// has already been rounded must survive being rounded again — otherwise each step would shave
    /// the number a little further.
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
    /// 🔒 <see cref="DeterminismRounding.IsRounded"/> is the predicate <c>Round</c>'s output
    /// satisfies and nothing else does — including the three values that are <em>not</em> rounding
    /// failures but must never be waved through.
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
