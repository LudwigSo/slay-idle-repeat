namespace SlayIdleRepeat.Core.Rules.Gear;

/// <summary>
/// The scalar every rolled item's stats are a multiple of: a fixed fraction of the chapter's power
/// target, scaled by the rarity band.
/// </summary>
/// <remarks>
/// <para>
/// Both inputs are authored and neither is restated here — the chapter's par power is read from the
/// par table and the band multiplier from the rarity ladder. The design set expects the flat slot
/// coefficients downstream of this to move, so a constant anywhere in this namespace would be a
/// second, frozen answer that no re-tune could reach.
/// </para>
/// <para>
/// It takes the chapter the item <em>originated</em> in rather than the chapter it is read in. An
/// item keeps the power it dropped with, which is why a merge takes the maximum origin of its inputs
/// rather than the first: without that rule, two identical-looking merges could differ by the whole
/// span of the chapter curve.
/// </para>
/// </remarks>
internal static class ItemPower
{
    /// <summary>The power one item of a band carries at a chapter.</summary>
    /// <remarks>
    /// The band is passed as its already-read multiplier rather than as the band itself. The two
    /// lookups — the chapter's par and the band's row — belong to the readers that own those tables,
    /// and keeping them out of this signature is what makes it three numbers and a product rather
    /// than a place a fourth table could quietly be consulted.
    /// </remarks>
    /// <param name="chapterPowerTarget">The chapter's par power. Positive and finite.</param>
    /// <param name="itemPowerCoefficient">The fraction of it one item carries. Positive and finite.</param>
    /// <param name="bandStatMultiplier">The rarity band's multiplier. Positive and finite.</param>
    /// <returns>The item's power scalar.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Any factor is not a positive finite number.</exception>
    internal static double For(
        double chapterPowerTarget, double itemPowerCoefficient, double bandStatMultiplier)
    {
        Positive(chapterPowerTarget, nameof(chapterPowerTarget));
        Positive(itemPowerCoefficient, nameof(itemPowerCoefficient));
        Positive(bandStatMultiplier, nameof(bandStatMultiplier));

        return chapterPowerTarget * itemPowerCoefficient * bandStatMultiplier;
    }

    private static void Positive(double factor, string parameter)
    {
        if (!double.IsFinite(factor) || factor <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                parameter,
                factor,
                "Item power is a product of three positive finite factors. A zero or negative one " +
                "would make every stat on the item zero or negative, and a non-finite one would make " +
                "the item unpersistable.");
        }
    }
}
