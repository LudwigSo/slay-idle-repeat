using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Gear;

/// <summary>Draws an item's affixes out of the pool it is eligible for.</summary>
/// <remarks>
/// <para>
/// It takes the eligible pool rather than working it out: the slot restriction that keeps lifesteal
/// off boots and the rarity floor that keeps the reroll-charge affix off the lower bands are both
/// authored per affix, and the tuning reader applies them where they are read. A roller that had to
/// remember either would eventually forget one.
/// </para>
/// <para>
/// <b>Drawn without replacement.</b> An item carrying the same affix twice would double one stat and
/// read to a player as a single unusually strong roll, which is why the instance refuses it outright;
/// this is the side that makes the refusal unreachable in practice.
/// </para>
/// <para>
/// <b>A short pool is refused, never quietly topped down.</b> A band asking for more affixes than its
/// slot's pool can supply is a content error, and answering with fewer would hide it behind an item
/// that merely looks unlucky.
/// </para>
/// <para>
/// 🔴 <b>The shipped pool is short by one for boots at the top band, and this refusal is what makes
/// that visible.</b> The top band rolls four affixes; exactly three of the fourteen may appear on
/// boots. So a top-band boots item cannot be minted at all, and roughly one top-band drop in six is a
/// boots item. Both halves are authored — the affix count and the slot restrictions — so neither can
/// be adjusted here without inventing content: a fifteenth boots-eligible affix and a slot-aware cap
/// on the top band's count are both authoring decisions, and picking one would freeze it before
/// anybody chose it. <b>Carried forward for a ruling, with the exact arithmetic pinned by
/// <c>GearMintingTests</c> so it goes red the day the pool is widened.</b>
/// </para>
/// </remarks>
internal static class GearAffixRoller
{
    /// <summary>Rolls an item's affixes.</summary>
    /// <remarks>
    /// One draw for the affix and one for its value, per affix, in that order — so the stream position
    /// after a roll is a function of the count alone and a replay lands in the same place.
    /// </remarks>
    /// <param name="pool">The affixes this item is eligible for. Never null; never mutated.</param>
    /// <param name="count">How many affixes to roll. Never negative.</param>
    /// <param name="draws">The already-opened draw stream, continued.</param>
    /// <returns>The rolled affixes, in roll order. Empty when the count is zero.</returns>
    /// <exception cref="ArgumentNullException">A reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="InvalidTunableException">The pool is smaller than the count.</exception>
    internal static IReadOnlyList<GearAffixRoll> Roll(
        IReadOnlyList<GearAffixDefinition> pool, int count, DeterministicRng draws)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(draws);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (count == 0)
        {
            return Array.Empty<GearAffixRoll>();
        }

        if (pool.Count < count)
        {
            throw new InvalidTunableException(
                DropsTuning.AffixesReference,
                $"This item rolls {AuthoredToken.Render(count)} affixes and only " +
                $"{AuthoredToken.Render(pool.Count)} in the pool are eligible for it. Rolling fewer " +
                "would be an item that merely looks unlucky, hiding a pool that cannot fill the band " +
                "at all.");
        }

        var remaining = new List<GearAffixDefinition>(pool);
        var rolled = new GearAffixRoll[count];

        for (var i = 0; i < count; i++)
        {
            var index = draws.Range(0, remaining.Count);
            var affix = remaining[index];
            remaining.RemoveAt(index);

            rolled[i] = new GearAffixRoll(affix.AffixId, ValueIn(affix, draws));
        }

        return Array.AsReadOnly(rolled);
    }

    /// <summary>
    /// A value uniformly inside the affix's authored range, rounded once at the roll.
    /// </summary>
    /// <remarks>
    /// Rounded here rather than at the record, because this is the accumulation point: the record
    /// refuses an unrounded value precisely so the rounding cannot quietly move somewhere the draw is
    /// no longer visible. A zero-width range still consumes its draw, so a re-tune that widens one
    /// does not shift every later affix on the same item.
    /// </remarks>
    private static double ValueIn(GearAffixDefinition affix, DeterministicRng draws)
    {
        var span = affix.Maximum - affix.Minimum;
        var unit = draws.NextDouble();

        return DeterminismRounding.Round(
            span <= 0.0 ? affix.Minimum : affix.Minimum + (span * unit));
    }
}
