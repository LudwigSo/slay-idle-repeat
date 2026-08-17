using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Rules.Luck;

/// <summary>
/// The <c>ENHANCE</c> class's guarantee: what one attempt's chance actually is once the item's run
/// of consecutive failures and any lucky bonus are applied.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A guarantee primitive, and it lives here for the same reason the other five do.</b> The
/// enhancement rate is where the <c>ENHANCE</c> guarantee fires, so it may be decided in exactly one
/// place; the forge asks the façade for a rate and never reaches the arithmetic itself. It is a
/// separate primitive from the mercy bank rather than a second entry point on it, because the two
/// mean different things — one raises a probability, the other spends tokens — and a type that did
/// both would let a caller reach the wrong one.
/// </para>
/// <para>
/// <b>The two additions are not the same addition.</b> The mercy slope is earned by the item, one
/// step per consecutive failure, and a success clears it. The lucky bonus is bought with an ad or
/// granted with a subscription, applies to one attempt, and — the authored rule — neither advances
/// nor consumes the mercy the item has built up. Adding them in one term would be arithmetically
/// identical and would lose exactly that distinction, so the two arrive as separate arguments and
/// the caller is told which counter its bonus moves.
/// </para>
/// <para>
/// <b>The cap is applied twice, and only the second application is load-bearing.</b>
/// <c>SoftPity.RateWithMercy</c> already clamps the mercy ramp at the same ceiling, so the earned
/// share arrives here at or below the cap; the clamp in <see cref="EffectiveRate"/> is there for the
/// bonus, which is added <em>after</em> that first clamp and would otherwise push the rate past a
/// ceiling the mercy alone was held below. The redundancy is stated rather than removed because the
/// two clamps belong to two different rules — one guards a ramp this type does not own, the other
/// guards an addition <c>SoftPity</c> never sees.
/// </para>
/// </remarks>
internal static class EnhanceMercy
{
    /// <summary>The effective chance of one enhancement attempt.</summary>
    /// <param name="baseRate">The level's unmodified chance, between 0 and 1.</param>
    /// <param name="consecutiveFailures">Failures on this gear instance since its last success.</param>
    /// <param name="luckyBonus">
    /// The one-attempt bonus, as a share. Stacks on top of the mercy and moves no counter.
    /// </param>
    /// <param name="rule">The authored slope and ceiling.</param>
    /// <returns>The chance the attempt is drawn against.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A probability is outside 0..1, the failure count is negative, or the bonus is not a
    /// non-negative finite share.
    /// </exception>
    internal static double EffectiveRate(
        double baseRate, int consecutiveFailures, double luckyBonus, EnhanceRule rule)
    {
        if (!double.IsFinite(luckyBonus) || luckyBonus < 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(luckyBonus),
                luckyBonus,
                "A lucky bonus is a non-negative finite share. A negative one would be a penalty " +
                "dressed as a reward, and nothing in the design set authors one.");
        }

        var earned = SoftPity.RateWithMercy(
            baseRate,
            consecutiveFailures,
            rule.MercySlopePerConsecutiveFailure,
            rule.EffectiveRateCap);

        return Math.Min(rule.EffectiveRateCap, earned + luckyBonus);
    }

    /// <summary>The mercy the item has earned, on its own — what a client shows beside the rate.</summary>
    /// <remarks>
    /// Answered rather than left to a caller subtracting two numbers: the display splits the bonus
    /// out (<em>"Success 41% (+16% mercy)"</em>), and a caller doing that subtraction itself would
    /// state the cap's effect a second time and disagree with this type the moment the ramp clips.
    /// </remarks>
    /// <param name="baseRate">The level's unmodified chance, between 0 and 1.</param>
    /// <param name="consecutiveFailures">Failures on this gear instance since its last success.</param>
    /// <param name="rule">The authored slope and ceiling.</param>
    /// <returns>The share the mercy actually contributes, after the ceiling.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A probability is outside 0..1, or the count is negative.</exception>
    internal static double EarnedShare(double baseRate, int consecutiveFailures, EnhanceRule rule) =>
        SoftPity.RateWithMercy(
            baseRate,
            consecutiveFailures,
            rule.MercySlopePerConsecutiveFailure,
            rule.EffectiveRateCap) - baseRate;

    /// <summary>The item's failure counter after an attempt.</summary>
    /// <param name="consecutiveFailures">The counter before the attempt.</param>
    /// <param name="succeeded">Whether the attempt succeeded.</param>
    /// <param name="carriedLuckyBonus">Whether the attempt carried a lucky bonus.</param>
    /// <param name="rule">The authored rule, which says whether such an attempt moves the counter.</param>
    /// <returns>The counter to store on the item.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The counter is negative.</exception>
    /// <remarks>
    /// A success clears the counter whether or not the attempt was helped: the run of failures is
    /// over either way, and leaving it standing would pay the player mercy for a streak that ended.
    /// </remarks>
    internal static int Advanced(
        int consecutiveFailures, bool succeeded, bool carriedLuckyBonus, EnhanceRule rule)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(consecutiveFailures);

        if (succeeded)
        {
            return 0;
        }

        return carriedLuckyBonus && !rule.AdEnhanceLuckAdvancesCounter
            ? consecutiveFailures
            : consecutiveFailures + 1;
    }
}
