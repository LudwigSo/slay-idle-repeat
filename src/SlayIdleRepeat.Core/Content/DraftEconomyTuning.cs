using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The perk draft's skip/reroll economy, read out of <c>tuning/currencies.json#/draftEconomy</c>.
/// </summary>
/// <remarks>
/// <see cref="RerollGoldCost"/> has no separately authored figure anywhere; it reuses the known
/// skip-reward magnitude as a placeholder for the balance team to retune, rather than inventing an
/// unrelated number.
/// </remarks>
internal sealed class DraftEconomyTuning
{
    /// <summary>The document the skip/reroll economy lives in.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    private const string Pointer = DocumentPath + "#/draftEconomy";

    /// <summary>The Gold <c>SKIP_DRAFT</c> pays the player.</summary>
    internal const string SkipGoldRewardReference = Pointer + "/skipGoldReward";

    /// <summary>The Gold <c>REROLL_DRAFT</c> costs the player.</summary>
    internal const string RerollGoldCostReference = Pointer + "/rerollGoldCost";

    private DraftEconomyTuning(long skipGoldReward, long rerollGoldCost)
    {
        SkipGoldReward = skipGoldReward;
        RerollGoldCost = rerollGoldCost;
    }

    /// <summary>The Gold reward for skipping a draft. 60 as shipped.</summary>
    internal long SkipGoldReward { get; }

    /// <summary>The Gold cost of a reroll. 60 as shipped.</summary>
    internal long RerollGoldCost { get; }

    /// <summary>Reads the draft economy block. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static DraftEconomyTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var skipGoldReward = content.ReadInt64(SkipGoldRewardReference);
        if (skipGoldReward <= 0)
        {
            throw new InvalidTunableException(
                SkipGoldRewardReference,
                "A skip reward is a positive Gold amount — its direction is fixed by " +
                "SKIP_DRAFT's own semantics, not the sign. This document authors " +
                skipGoldReward.ToString(CultureInfo.InvariantCulture) + ".");
        }

        var rerollGoldCost = content.ReadInt64(RerollGoldCostReference);
        if (rerollGoldCost <= 0)
        {
            throw new InvalidTunableException(
                RerollGoldCostReference,
                "A reroll cost is a positive Gold amount. This document authors " +
                rerollGoldCost.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return new DraftEconomyTuning(skipGoldReward, rerollGoldCost);
    }
}
