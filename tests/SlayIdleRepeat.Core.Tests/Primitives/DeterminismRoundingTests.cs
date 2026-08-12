using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Primitives;

/// <summary>
/// 🔒 `05` §1.1's 4-decimal-place rule, pinned <b>numerically</b> at the one place it is now stated.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Why this suite exists separately from the architecture rule.</b>
/// <c>DeterminismRoundingRuleTests</c> is an IL scan: it asserts <em>where</em> the rule is stated
/// and says nothing about what it computes. Every other caller — <c>StatRounding</c>,
/// <c>OpRounding</c>, <c>ValueScale</c>, <c>ConditionEvaluator</c> — refuses NaN and infinity
/// <em>before</em> reaching the primitive, so their suites cannot see its behaviour on either. The
/// two sites that <b>do</b> depend on it are <c>CanonicalStateWriter</c> and <c>CombatLog</c>, whose
/// rounding guards are <c>DeterminismRounding.Round(value) != value</c> — and that guard only fires
/// on a NaN because <c>Round(NaN)</c> is a NaN and <c>NaN != NaN</c>. Make <c>Round</c> "helpfully"
/// clamp a NaN to zero and both guards go quiet with every other test in the repository still green.
/// This suite is what stops that.
/// </para>
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
    /// 🔒 <b>Midpoints go to even</b>, which is <c>Math.Round</c>'s default and is now written out —
    /// so the rule no longer depends on six authors agreeing about a default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>A decimal literal ending in 5 is usually NOT a midpoint</b>, and the two rows that look
    /// like counterexamples are the point of the theory. <c>0.00005</c> and <c>0.12345</c> have
    /// nearest <c>double</c>s just <em>below</em> their decimal midpoint and round down;
    /// <c>1.00005</c> and <c>0.12355</c> have nearest <c>double</c>s just <em>above</em> theirs and
    /// round up. None of the four is decided by the midpoint mode at all — they are decided by
    /// binary representation, identically on every IEEE-754 platform, which is what makes `05` §1.1 a
    /// determinism rule rather than a rounding preference.
    /// </para>
    /// <para>
    /// The values are pinned as computed so that a future edit to <c>Round</c> — a different mode, a
    /// different constant, an added epsilon — moves at least one of them.
    /// <c>StatAggregationTests</c> independently asserts <c>1.00005 → 1.0001</c> through the whole
    /// `18` §8 pipeline.
    /// </para>
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
