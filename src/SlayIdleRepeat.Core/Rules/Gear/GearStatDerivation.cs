using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model.Gear;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Gear;

/// <summary>One derived stat of an item: which stat, how much of it, and whether it is a percentage.</summary>
/// <param name="Stat">The authored stat token, e.g. <c>ATK</c>.</param>
/// <param name="Value">The derived magnitude, rounded to the assembly's determinism precision.</param>
/// <param name="IsPercent">
/// Whether the value is a fraction feeding a capped percentage rather than a flat amount. A flat stat
/// scales with chapter through item power; a percent stat scales with rarity alone, so the two must
/// never be added into the same accumulator by accident.
/// </param>
internal readonly record struct DerivedGearStat(string Stat, double Value, bool IsPercent);

/// <summary>
/// The two stats a gear instance contributes, derived from its stored fields — never read back from
/// storage, because they are never stored.
/// </summary>
/// <remarks>
/// <para>
/// This is what "computed stats are never stored" costs and buys: every number a hero screen shows
/// for an item is recomputed here from the base item, the band, the chapter of origin and the one
/// quality scalar. Saves stay small, and a balance patch re-tunes items a player already owns.
/// </para>
/// <para>
/// <b>Two kinds of stat, and the difference is load-bearing.</b> A flat stat is a multiple of item
/// power and therefore climbs with the chapter curve. A percent stat is read straight out of the
/// per-rarity table and climbs with rarity alone — it feeds a capped percentage, and letting it
/// inflate across chapters would push every build against the cap by mid-game. The slot table says
/// which is which by authoring a null coefficient for the percent ones.
/// </para>
/// <para>
/// Quality applies to both kinds, and asymmetrically: the primary position spans a narrower range
/// than the secondary, so a high-quality item leans hardest into its secondary stat. That is what
/// makes two S-rarity blades different items, and it is read from the tuning rather than written
/// here.
/// </para>
/// <para>
/// ⚠️ <b>Enhancement is not applied here.</b> The level is stored on the instance and the ladder that
/// prices it belongs to the forge; this derivation answers what the item rolled, not what it has been
/// upgraded to. A caller that needs the enhanced figure composes the two rather than finding one of
/// them silently folded in.
/// </para>
/// </remarks>
internal static class GearStatDerivation
{
    /// <summary>The item's primary stat, at its rolled quality.</summary>
    /// <param name="par">The par table.</param>
    /// <param name="drops">The gear tables.</param>
    /// <param name="item">The rolled item.</param>
    /// <returns>The derived primary stat.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static DerivedGearStat Primary(ParPowerTuning par, DropsTuning drops, GearInstance item)
    {
        ArgumentNullException.ThrowIfNull(drops);
        ArgumentNullException.ThrowIfNull(item);

        var row = drops.Coefficients(item.Slot);

        return Derive(
            par,
            drops,
            item,
            row.PrimaryStat,
            row.PrimaryCoefficient,
            drops.Quality.Primary(item.Quality));
    }

    /// <summary>The item's secondary stat, at its rolled quality.</summary>
    /// <param name="par">The par table.</param>
    /// <param name="drops">The gear tables.</param>
    /// <param name="item">The rolled item.</param>
    /// <returns>The derived secondary stat.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    internal static DerivedGearStat Secondary(ParPowerTuning par, DropsTuning drops, GearInstance item)
    {
        ArgumentNullException.ThrowIfNull(drops);
        ArgumentNullException.ThrowIfNull(item);

        var row = drops.Coefficients(item.Slot);

        return Derive(
            par,
            drops,
            item,
            row.SecondaryStat,
            row.SecondaryCoefficient,
            drops.Quality.Secondary(item.Quality));
    }

    /// <summary>
    /// A derived stat as the hero actually receives it: the item's own figure scaled by the Forge's
    /// enhancement multiplier, rounded once.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>One place applies the multiplier, and the hero build, the comparison and the inventory
    /// projection all call it.</b> <see cref="Primary"/> and <see cref="Secondary"/> deliberately answer
    /// the UNENHANCED figure — enhancement is the Forge's and the derivation is the drop table's — so
    /// the fold used to live only in the gear effect source. A comparison that read the raw figures
    /// beside a hero that read the folded ones would show a +10 blade tying a +0 one while the fight
    /// said otherwise, which is the exact disagreement the projection exists to rule out.
    /// </remarks>
    /// <param name="derived">The unenhanced figure, from <see cref="Primary"/> or <see cref="Secondary"/>.</param>
    /// <param name="forge">The Forge tuning the multiplier ladder is read from.</param>
    /// <param name="enhanceLevel">The item's <c>+N</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="forge"/> is null.</exception>
    internal static DerivedGearStat AsWorn(DerivedGearStat derived, ForgeTuning forge, int enhanceLevel)
    {
        ArgumentNullException.ThrowIfNull(forge);

        return derived with
        {
            Value = DeterminismRounding.Round(derived.Value * forge.StatMultiplier(enhanceLevel)),
        };
    }

    private static DerivedGearStat Derive(
        ParPowerTuning par,
        DropsTuning drops,
        GearInstance item,
        string stat,
        double? coefficient,
        double qualityScale)
    {
        ArgumentNullException.ThrowIfNull(par);

        var isPercent = coefficient is null;

        var basis = isPercent
            ? drops.PercentStat(stat, item.Rarity)
            : ItemPower.For(
                  par.ChapterPowerTarget(item.ChapterOrigin),
                  drops.ItemPowerCoefficient,
                  drops.Band(item.Rarity).StatMultiplier) * coefficient!.Value;

        return new DerivedGearStat(stat, DeterminismRounding.Round(basis * qualityScale), isPercent);
    }
}
