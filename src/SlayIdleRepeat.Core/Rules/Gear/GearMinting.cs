using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Content.Gear;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Gear;

/// <summary>
/// Builds one item of an <em>already-decided</em> rarity: the base item, the quality scalar and the
/// affixes.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A type of its own, and the split is the routing rule rather than a preference.</b> Every
/// method that answers a gear instance is a producer as far as `24` §11 is concerned, and this one
/// genuinely decides nothing protected — the band arrives already resolved. The rule's exemptions are
/// per type, so keeping minting here is what lets <c>GearGeneration</c>, which does make the
/// protected decision, stay unexempted and fail the day it stops routing through the façade.
/// </para>
/// <para>
/// <b>Draw order is the item's identity.</b> Base item, then quality, then affixes — fixed, so the
/// same seed at the same stream position mints the same item on the client and on the server, and a
/// forced band and a natural one leave the stream in the same place.
/// </para>
/// <para>
/// <b>The base item is drawn uniformly.</b> Nothing in the design set weights one of the twenty-four
/// over another for a drop, and the one mechanism that biases the identity roll — targeted acquisition
/// — is a later task's and is deliberately not anticipated: a weight invented now would be
/// indistinguishable from the authored one when it arrives.
/// </para>
/// </remarks>
internal static class GearMinting
{
    /// <summary>Mints one item at a given band.</summary>
    /// <param name="instanceId">The identity the server minted for this item.</param>
    /// <param name="catalogue">The twenty-four base items.</param>
    /// <param name="drops">The gear tables.</param>
    /// <param name="chapterOrigin">The chapter the item is scaled against, from 1.</param>
    /// <param name="rarity">The band the item lands on, decided elsewhere.</param>
    /// <param name="draws">The already-opened draw stream, continued.</param>
    /// <returns>The item.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    internal static GearInstance Mint(
        GearInstanceId instanceId,
        GearCatalogue catalogue,
        DropsTuning drops,
        int chapterOrigin,
        Rarity rarity,
        DeterministicRng draws)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(drops);
        ArgumentNullException.ThrowIfNull(draws);

        var definition = catalogue.Definitions[draws.Range(0, catalogue.Definitions.Count)];
        var quality = RollQuality(drops.Quality, draws);

        var affixes = GearAffixRoller.Roll(
            drops.EligibleAffixes(definition.Slot, rarity),
            drops.Band(rarity).AffixCount,
            draws);

        return new GearInstance(
            instanceId,
            definition.DefId,
            definition.Slot,
            definition.Family,
            rarity,
            chapterOrigin,
            quality,
            GearInstance.Unenhanced,
            GearInstance.NoFailures,
            affixes,
            locked: false);
    }

    /// <summary>
    /// The one quality scalar, drawn uniformly across the authored range and rounded once.
    /// </summary>
    /// <remarks>
    /// Rounded here because this is where the draw becomes state. The instance refuses an unrounded
    /// quality, which is what keeps the rounding from drifting to a caller that no longer has the
    /// draw in front of it.
    /// </remarks>
    private static double RollQuality(QualityScales scales, DeterministicRng draws) =>
        DeterminismRounding.Round(
            scales.Minimum + ((scales.Maximum - scales.Minimum) * draws.NextDouble()));
}
