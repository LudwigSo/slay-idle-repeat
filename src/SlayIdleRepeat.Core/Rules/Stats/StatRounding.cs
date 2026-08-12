using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Stats;

/// <summary>
/// 🔒 The 4-decimal-place rounding of `05` §1.1, in one place.
/// </summary>
/// <remarks>
/// <para>
/// `05` §1.1, verbatim: <em>"all combat math uses <c>double</c>, <b>rounded to 4 decimal places
/// (<c>Math.Round(x, 4)</c>) at every accumulation point</b> — after each damage calculation, each
/// heal, and each stat aggregation step. This is the locked determinism rule (`14` §8.2, `18` §8
/// step 10, `16` A3)."</em> `18` §8 states the same rule as its step 10.
/// </para>
/// <para>
/// ⚠️ <b>Every stat aggregation STEP, not only step 10.</b> The two are different answers, and
/// <c>StatAggregationTests.Rounding_at_step_5_and_rounding_only_at_step_10_are_different_answers</c>
/// exhibits a case where they differ in the fourth decimal place. Rounding only at the end would
/// make the pipeline's arithmetic depend on how many intermediate products the platform happened to
/// keep in an 80-bit register, which is the class of divergence `14` §8.2 exists to remove.
/// </para>
/// <para>
/// ⚠️ <b>Where it is <em>observable</em>, said honestly.</b> A rounding at step <em>n</em> can only
/// change an answer if a later step magnifies the difference, so steps 4, 5, 6 and 7 each have a
/// discriminating case in <c>StatAggregationTests</c> and steps 8, 9 and 10 do not: they are
/// terminal, and only the last of the three is observable. All seven are written anyway, because
/// `05` §1.1 says "every accumulation point" and `18` §8 lists step 10 separately — a future step
/// inserted between 8 and 10 should not have to discover that its predecessor stopped rounding.
/// </para>
/// <para>
/// 🔒 <b>The trailing <c>+ 0.0</c> is not redundant</b>, and it is the same normalisation
/// <see cref="ValueScale.EffectiveValue"/> performs. <c>Math.Round(-0.00004, 4)</c> is <c>-0.0</c>
/// and .NET preserves the sign of zero, so a stat that drifts a hair below zero — a `18` §7.10 Bog
/// Air <c>-0.35</c> against a small base, a <c>SUNDER</c> stack that takes DEF through zero — would
/// reach <c>CanonicalStateWriter</c> as a negative zero. That writer <em>throws</em> rather than
/// encoding one, because <c>-0.0 == 0.0</c> is true in C# while the bit patterns differ, so two
/// states the language calls identical would carry different hashes. Its own comment names this
/// method's job: <em>"normalise at the accumulation point — <c>x + 0.0</c> is +0.0 — rather than
/// letting the writer edit state on its way out."</em>
/// </para>
/// <para>
/// NaN and infinity throw here rather than at the writer. An infinite stat is an overflow upstream,
/// and a NaN is a <c>0 × ∞</c> or <c>0/0</c> in an aggregation step; both are far easier to trace
/// from the step and stat that produced them than from a serialisation failure three layers later.
/// </para>
/// </remarks>
internal static class StatRounding
{
    /// <summary>🔒 The number of decimal places `05` §1.1 and `18` §8 step 10 lock.</summary>
    internal const int Decimals = 4;

    /// <summary>
    /// Rounds one accumulated stat value to <see cref="Decimals"/> places and normalises
    /// <c>-0.0</c> to <c>+0.0</c>.
    /// </summary>
    /// <param name="value">The accumulated value.</param>
    /// <param name="stat">The stat being accumulated — named in the failure message.</param>
    /// <param name="step">The `18` §8 step this accumulation point belongs to.</param>
    /// <exception cref="ArithmeticException">
    /// <paramref name="value"/> is NaN or infinite.
    /// </exception>
    internal static double Round(double value, StatId stat, string step)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArithmeticException(
                $"18 §8 {step} produced {value.ToString("R", CultureInfo.InvariantCulture)} for {stat}. " +
                "A stat that is NaN or infinite is an overflow or a 0/0 in this step, not a value to " +
                "round and carry forward — 05 §1.1's determinism rule has nothing to say about it and " +
                "CanonicalStateWriter would refuse it later, naming the serialiser rather than the " +
                "aggregation step that produced it.");
        }

        // 🔒 `+ 0.0` turns -0.0 into +0.0 and changes nothing else. See the remarks.
        return Math.Round(value, Decimals) + 0.0;
    }

    /// <summary>
    /// True when a value is already in the form <see cref="Round"/> produces: rounded to
    /// <see cref="Decimals"/> places, finite, and not a negative zero.
    /// </summary>
    /// <remarks>
    /// The guard <see cref="ActorStats"/> states over every value it holds, so that "an unstated
    /// stat is a bug" is joined by "an unrounded stat is a bug". NaN answers <c>false</c>, because
    /// <c>Math.Round(NaN, 4) != NaN</c> — deliberately, since a NaN is exactly the value that must
    /// not be waved through.
    /// </remarks>
    internal static bool IsRounded(double value) =>
        double.IsFinite(value) &&
        Math.Round(value, Decimals) == value &&
        !(double.IsNegative(value) && value == 0.0);
}
