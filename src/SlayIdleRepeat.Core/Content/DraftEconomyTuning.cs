using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 M3-06, `06` §1 — the perk draft's skip/reroll economy, read out of
/// <c>tuning/currencies.json#/draftEconomy</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Authored by this task; no such block existed before it.</b> <c>SkipDraftCommand</c>'s own
/// XML doc comment cited "`06` §1: skipping is allowed and pays 60 Gold plus a free reroll" as
/// prose — nothing in <c>currencies.json</c> or any other tuning file carried the number.
/// <see cref="SkipGoldReward"/> is that one figure, transcribed rather than re-derived.
/// <see cref="RerollGoldCost"/> has no authored figure anywhere in the repository; this is the
/// judgment call recorded in the task's own report: the smallest defensible extension is to reuse
/// the one known magnitude (60) rather than invent an unrelated second number, flagged here as a
/// placeholder for the balance team to retune against the simulator (`10` §9), not a design ruling.
/// </para>
/// <para>
/// ⚠️ <b>The "free reroll" half of the doc comment is deliberately not modelled as a mechanic.</b>
/// <c>SkipDraftCommand</c>'s own summary is "take none of the offered perks" — a close of the
/// draft, not a continuation of it — so this task reads "plus a free reroll" as descriptive colour
/// for the fact a skipped draft costs the player nothing but Gold (no perk lost, another draft
/// arrives after the next win), rather than as a second currency-free reroll grant that would need
/// its own persisted state. Recorded as an assumption, not silently resolved.
/// </para>
/// </remarks>
internal sealed class DraftEconomyTuning
{
    /// <summary>The document `06` §1's skip/reroll economy lives in.</summary>
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

    /// <summary>`06` §1 — the Gold reward for skipping a draft. 60 as shipped.</summary>
    internal long SkipGoldReward { get; }

    /// <summary>M3-06's judgment call — the Gold cost of a reroll. 60 as shipped.</summary>
    internal long RerollGoldCost { get; }

    /// <summary>Reads the draft economy block. Throws rather than defaulting on anything unusable.</summary>
    /// <param name="content">The version-stamped snapshot the command is reading (`30` §3).</param>
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
