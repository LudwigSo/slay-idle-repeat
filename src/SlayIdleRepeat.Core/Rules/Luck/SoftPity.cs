namespace SlayIdleRepeat.Core.Rules.Luck;

/// <summary>
/// The optional ramp that makes the guarantee feel earned rather than granted: past a threshold of
/// misses, the target's odds climb draw by draw until the hard guarantee catches them.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two different arithmetics live here, and they are deliberately not folded into one.</b> The
/// chest/crate ramp is a <em>multiplier on a draw weight</em>; the enhancement ramp is an
/// <em>addition to a success probability</em>, with a cap. Expressing the second as the first would
/// be a lie about the numbers — a weight multiplier renormalises against its neighbours and a rate
/// addition does not — so each is stated in the shape its own tuning block authors it in.
/// </para>
/// <para>
/// Neither ramp touches a counter. Advancing and resetting are the hard guarantee's, so a soft-pity
/// change can be re-tuned without moving a single stored counter.
/// </para>
/// </remarks>
internal static class SoftPity
{
    /// <summary>
    /// The multiplier applied to the target rarity's draw weight after <paramref name="misses"/>
    /// misses: flat at 1 until the threshold, then climbing linearly with the authored slope.
    /// </summary>
    /// <remarks>
    /// <paramref name="misses"/> is the counter's value <em>before</em> the draw, so the draw about
    /// to be made is the (misses + 1)-th since the last reset.
    /// </remarks>
    /// <param name="misses">Draws since the target's counter last reset. Never negative.</param>
    /// <param name="threshold">The authored miss threshold the ramp starts past. Never negative.</param>
    /// <param name="slope">The authored per-miss growth. Positive.</param>
    /// <returns>The weight multiplier. Never below 1.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A count is negative, or <paramref name="slope"/> is not a positive finite number.
    /// </exception>
    internal static double WeightMultiplier(int misses, int threshold, double slope)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(misses);
        ArgumentOutOfRangeException.ThrowIfNegative(threshold);
        RequirePositiveSlope(slope);

        return Flat + (slope * Math.Max(0, misses - threshold));
    }

    /// <summary>
    /// The effective success probability after a run of consecutive failures: the base rate plus the
    /// authored slope per failure, clamped at the authored cap.
    /// </summary>
    /// <remarks>
    /// Additive on a probability, not multiplicative on a weight — see this type's remarks. The cap
    /// is authored rather than assumed to be 1, so a block may stop the ramp short of certainty.
    /// </remarks>
    /// <param name="baseRate">The unmodified success probability, between 0 and 1.</param>
    /// <param name="consecutiveFailures">Failures since the last success. Never negative.</param>
    /// <param name="slope">The authored per-failure addition. Positive.</param>
    /// <param name="cap">The authored ceiling on the effective rate, between 0 and 1.</param>
    /// <returns>The effective rate.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A probability is outside 0..1, a count is negative, or <paramref name="slope"/> is not a
    /// positive finite number.
    /// </exception>
    internal static double RateWithMercy(
        double baseRate, int consecutiveFailures, double slope, double cap)
    {
        RequireProbability(baseRate, nameof(baseRate));
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailures);
        RequirePositiveSlope(slope);
        RequireProbability(cap, nameof(cap));

        return Math.Min(cap, baseRate + (slope * consecutiveFailures));
    }

    /// <summary>The multiplier a curve carries before its threshold is passed.</summary>
    private const double Flat = 1.0;

    private static void RequirePositiveSlope(double slope)
    {
        if (!double.IsFinite(slope) || slope <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(slope),
                slope,
                "A ramp's slope is a positive finite number. A slope of zero is a curve that does " +
                "not ramp, which is authored as no curve at all rather than as a flat one.");
        }
    }

    private static void RequireProbability(double value, string parameter)
    {
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                parameter, value, "A probability is a finite number between 0 and 1 inclusive.");
        }
    }
}
