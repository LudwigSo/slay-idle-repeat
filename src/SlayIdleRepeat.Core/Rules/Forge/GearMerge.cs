using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Rules.Luck;

namespace SlayIdleRepeat.Core.Rules.Forge;

/// <summary>
/// Fusing identical items into one of the next band: what makes a selection legal, and what the item
/// it produces carries.
/// </summary>
/// <remarks>
/// <para>
/// <b>Nothing protected is decided here.</b> The band the output lands on comes back from the luck
/// façade, which is the one place a grant band is decided — a fusion draws no rarity, but it hands
/// the player an item all the same, and stating its band here would be a second place a grant band
/// comes from.
/// </para>
/// <para>
/// 🔒 <b>Every carried-forward value is a maximum over the REAL inputs.</b> Quality, chapter of
/// origin and the mercy counter are all taken as the highest of the items actually consumed, and a
/// dust-filled slot contributes to none of them. The chapter rule is the load-bearing one: item power
/// doubles per chapter, so without it two fusions of what look like the same three items could
/// legally differ by a factor of sixteen depending on which of them the code happened to read.
/// </para>
/// <para>
/// <b>The output keeps the first input's identity.</b> Three items go in and one comes out, so one of
/// the three identities survives and no new one is minted — which matters because the domain has no
/// way to mint one: identity is a port, and a rule may not name a port. The surviving identity is the
/// first the client named, so the same selection always produces the same identity.
/// </para>
/// <para>
/// <b>It writes nothing.</b> It answers the refusal, or the item; consuming the inputs, charging for
/// the fusion and filing the output are the handler's, on the same stateless contract the luck façade
/// keeps.
/// </para>
/// </remarks>
internal static class GearMerge
{
    /// <summary>Why this selection is not a legal fusion, or <c>null</c> when it is.</summary>
    /// <param name="inputs">The real items being fused, in the order the client named them.</param>
    /// <param name="dustSubstituted">Whether Merge Dust fills one of the slots.</param>
    /// <param name="tuning">The forge numbers — how many inputs, and how many dust may fill.</param>
    /// <returns>The rule that refuses it, or <c>null</c>.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    internal static MergeRefusal? Refusal(
        IReadOnlyList<GearInstance> inputs, bool dustSubstituted, ForgeTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(tuning);

        var dustSlots = dustSubstituted ? 1 : 0;

        if (dustSlots > tuning.MergeDustSubstituteMaxInputs)
        {
            return MergeRefusal.DUST_SUBSTITUTION_NOT_ALLOWED;
        }

        if (inputs.Count + dustSlots != tuning.MergeInputCount || inputs.Count == 0)
        {
            return MergeRefusal.WRONG_INPUT_COUNT;
        }

        var first = inputs[0];

        for (var i = 1; i < inputs.Count; i++)
        {
            var other = inputs[i];

            for (var earlier = 0; earlier < i; earlier++)
            {
                if (inputs[earlier].InstanceId.Equals(other.InstanceId))
                {
                    return MergeRefusal.DUPLICATE_INPUT;
                }
            }

            if (!string.Equals(first.DefId, other.DefId, StringComparison.Ordinal))
            {
                return MergeRefusal.MISMATCHED_ITEM;
            }

            if (first.Rarity != other.Rarity)
            {
                return MergeRefusal.MISMATCHED_RARITY;
            }

            if (first.EnhanceLevel != other.EnhanceLevel)
            {
                return MergeRefusal.MISMATCHED_ENHANCE_LEVEL;
            }
        }

        return LuckService.MergeOutputBand(first.Rarity) is null
            ? MergeRefusal.NO_HIGHER_RARITY
            : null;
    }

    /// <summary>Fuses a selection <see cref="Refusal"/> has already accepted.</summary>
    /// <param name="inputs">The real items being fused, in the order the client named them.</param>
    /// <param name="drops">The gear tables — the band's affix count and the eligible pool.</param>
    /// <param name="draws">The already-opened draw stream, continued. Only the affixes are drawn.</param>
    /// <returns>The fused item.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="inputs"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">
    /// The inputs are at the top of the ladder, so there is no band to fuse onto. A caller reaching
    /// here has skipped <see cref="Refusal"/>.
    /// </exception>
    internal static GearInstance Fuse(
        IReadOnlyList<GearInstance> inputs, DropsTuning drops, DeterministicRng draws)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(drops);
        ArgumentNullException.ThrowIfNull(draws);

        if (inputs.Count == 0)
        {
            throw new ArgumentException(
                "A fusion of no items has no base item, no band and no identity to give its output. " +
                "The selection is checked by Refusal before it reaches here.",
                nameof(inputs));
        }

        var first = inputs[0];

        var band = LuckService.MergeOutputBand(first.Rarity) ?? throw new InvalidOperationException(
            "'" + first.Rarity + "' is the top of the ladder, so there is no band above it to fuse " +
            "onto. Refusal answers NO_HIGHER_RARITY for exactly this selection, and reaching here " +
            "means a caller skipped it.");

        var quality = first.Quality;
        var chapterOrigin = first.ChapterOrigin;
        var failures = first.EnhanceFailures;

        for (var i = 1; i < inputs.Count; i++)
        {
            quality = Math.Max(quality, inputs[i].Quality);
            chapterOrigin = Math.Max(chapterOrigin, inputs[i].ChapterOrigin);
            failures = Math.Max(failures, inputs[i].EnhanceFailures);
        }

        var eligible = drops.EligibleAffixes(first.Slot, band);

        return new GearInstance(
            first.InstanceId,
            first.DefId,
            first.Slot,
            first.Family,
            band,
            chapterOrigin,
            quality,
            first.EnhanceLevel,
            failures,
            GearAffixRoller.Roll(eligible, GearMinting.AffixCountFor(drops, band, eligible.Count), draws),
            locked: false);
    }
}
