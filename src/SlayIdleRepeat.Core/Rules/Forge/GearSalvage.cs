using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Forge;

/// <summary>What breaking an item down pays: Merge Dust, and a share of the stones its level cost.</summary>
/// <remarks>
/// <para>
/// <b>Two currencies and nothing else.</b> The design set additionally makes the top band yield a Set
/// Token, and that half is deliberately absent: Set Tokens are a non-wallet counter that nothing on
/// the player holds yet, and the operation that spends them is not built. Paying one into a column
/// that does not exist is not a thing this rule can do, and inventing the column here would put the
/// token economy's first statement inside a salvage payout.
/// </para>
/// <para>
/// 🔴 <b>Both payouts round DOWN, and neither the rounding nor its direction is authored.</b> The
/// formulas are stated over real numbers — a base multiplied by a share — while both currencies are
/// whole. Down rather than nearest, because a refund is a partial one by construction and the
/// alternative pays a player more stones than an item cost at some levels, which turns a sink into a
/// very slow source. The choice is recorded here because it is a choice.
/// </para>
/// <para>
/// 🔴 <b>And it is the ONLY whole-currency payout in the game that floors — recorded because "this
/// is unauthored" and "this is the outlier" are two different facts, and the paragraph above only
/// stated the first.</b> Every other real-to-whole currency conversion in <c>Core</c> rounds to
/// nearest: <c>ChapterScalarTuning</c>, <c>MinigameRewardTuning</c>, <c>RunXpTuning</c> and
/// <c>Rules.Economy.RunRewardMath</c> all take <c>MidpointRounding.AwayFromZero</c>, and
/// <c>Rules.Economy.ShopPricing</c> takes <c>ToEven</c> in <c>decimal</c>. Nothing in the design set
/// authors any of the six directions, so this is not a divergence from a stated rule — it is six
/// independent unauthored choices, of which this one went the other way.
/// </para>
/// <para>
/// <b>The reasoning that makes the divergence deliberate is sink-versus-source, and it does not
/// generalise.</b> The five that round to nearest are <em>sources</em>: they convert an authored
/// reward into a payout, and a half-unit either way is a rounding error in the player's favour half
/// the time. Salvage is the return leg of a <em>sink</em> — it hands back part of what the player
/// already spent — so rounding it up pays out material that was never paid in, and at some levels
/// the stone refund would exceed the stone cost outright. Rounding up a reward is generous;
/// rounding up a refund is a leak. If a later task unifies the six, this is the one that must not be
/// unified <em>towards</em> the others without a ruling on the refund ladder first.
/// </para>
/// <para>
/// 🔒 <b>The product is put through the determinism rounding before the floor, and the flooring
/// alone is wrong without it.</b> A share authored at two decimal places is not exact in binary, so
/// a product whose real value is a whole number can land a fraction of an ulp below it: at the
/// shipped numbers a B-band item at +9 is worth exactly 94 dust and the bare product is
/// 93.999999999999986, which floors to 93. Rounding to the assembly's 4 decimal places first lands
/// the value on the number the document states, and leaves a genuinely fractional payout — 11.5,
/// 3.6 — to floor as it should.
/// </para>
/// </remarks>
internal static class GearSalvage
{
    /// <summary>What this item breaks down into.</summary>
    /// <param name="item">The item being salvaged.</param>
    /// <param name="tuning">The forge numbers.</param>
    /// <returns>The Merge Dust and the Enhance Stones the player is paid.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="InvalidTunableException">The document values no item of that band.</exception>
    internal static (long Dust, long Stones) Payout(GearInstance item, ForgeTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(tuning);

        return (
            Dust(tuning.SalvageBaseDust(item.Rarity), item.EnhanceLevel, tuning.DustPerEnhanceLevel),
            Stones(tuning.EnhanceStonesInvested(item.EnhanceLevel), tuning.StoneRefundShare));
    }

    /// <summary>The dust an item of a given base value and level breaks down into.</summary>
    /// <param name="baseDust">The band's base dust.</param>
    /// <param name="enhanceLevel">The level the item stands at.</param>
    /// <param name="perLevelShare">The share of the base dust each level adds back.</param>
    /// <returns>The dust, rounded down.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is negative.</exception>
    internal static long Dust(long baseDust, int enhanceLevel, double perLevelShare)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(baseDust);
        ArgumentOutOfRangeException.ThrowIfNegative(enhanceLevel);
        ArgumentOutOfRangeException.ThrowIfNegative(perLevelShare);

        return Whole(baseDust * (1.0 + (perLevelShare * enhanceLevel)));
    }

    /// <summary>The stones a refund share returns out of what was invested.</summary>
    /// <param name="invested">The stones the item's level cost.</param>
    /// <param name="refundShare">The share returned, between 0 and 1.</param>
    /// <returns>The refund, rounded down.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is outside its stated range.</exception>
    internal static long Stones(long invested, double refundShare)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(invested);

        if (!double.IsFinite(refundShare) || refundShare is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(refundShare),
                refundShare,
                "A refund share lies in [0,1]. A share above one would pay back more stones than the " +
                "item ever cost, which turns the game's largest material sink into a source.");
        }

        return Whole(invested * refundShare);
    }

    /// <summary>One payout as a whole number: rounded to the assembly's precision, then floored.</summary>
    private static long Whole(double payout) =>
        (long)Math.Floor(DeterminismRounding.Round(payout));
}
