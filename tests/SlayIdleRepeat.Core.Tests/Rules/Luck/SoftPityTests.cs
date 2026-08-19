using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rng;
using SlayIdleRepeat.Core.Rules.Luck;
using SlayIdleRepeat.Core.Tests.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Luck;

/// <summary>
/// M2, the two ramps: the weight multiplier <c>1 + k·max(0, misses − T)</c> that chests and crates
/// author, and the additive rate mercy <c>min(cap, base + slope × failures)</c> that enhancement
/// authors — kept apart, because folding one into the other would be a lie about the arithmetic.
/// </summary>
/// <remarks>
/// Values are compared after <c>DeterminismRounding.Round</c>, the project's own 4-decimal rule:
/// <c>1 + 0.05 × 58</c> is <c>3.9000000000000004</c> in binary floating point, and asserting the raw
/// double would pin the representation rather than the number.
/// </remarks>
public sealed class SoftPityTests
{
    // ---------------------------------------------------------------- the weight multiplier

    /// <summary>Below and at the threshold the curve is flat: no ramp, no rounding drift, exactly 1.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(99)]
    [InlineData(100)]
    public void The_ramp_is_flat_at_one_until_the_threshold_is_passed(int misses)
    {
        Rounded(SoftPity.WeightMultiplier(
                misses,
                LuckDocuments.ShippedChestStandardSoftPityThreshold,
                LuckDocuments.ShippedChestStandardSoftPitySlope))
            .ShouldBe(
                1.0,
                "24 §1 M2: the multiplier is 1 + k·max(0, misses − T). At or below T the max clamps " +
                "to zero, so the SS share is the base share and 24 §2's 'pity is not a rate " +
                "increase' still holds for the first hundred chests.");
    }

    /// <summary>Past the threshold it climbs linearly, one slope per miss.</summary>
    [Theory]
    [InlineData(101, 1.05)]
    [InlineData(110, 1.5)]
    [InlineData(120, 2.0)]
    [InlineData(158, 3.9)]
    public void The_ramp_climbs_by_one_slope_per_miss_past_the_threshold(int misses, double expected)
    {
        Rounded(SoftPity.WeightMultiplier(
                misses,
                LuckDocuments.ShippedChestStandardSoftPityThreshold,
                LuckDocuments.ShippedChestStandardSoftPitySlope))
            .ShouldBe(expected);
    }

    /// <summary>
    /// The document's own arithmetic claim, pinned: <em>"the SS chance begins climbing from chest 101
    /// and reaches roughly 4× base by chest 159"</em>.
    /// </summary>
    /// <remarks>
    /// Chest #159 is taken with 158 misses on the clock, so the multiplier is
    /// <c>1 + 0.05 × (158 − 100) = 3.9</c>. It is the one place <c>24</c> checks its own numbers, and
    /// an off-by-one in what <c>misses</c> counts would answer 3.95 here and stay plausible.
    /// </remarks>
    [Fact]
    public void The_hundred_and_fifty_ninth_chest_sits_at_roughly_four_times_base()
    {
        var chest = 159;

        Rounded(SoftPity.WeightMultiplier(
                chest - 1,
                LuckDocuments.ShippedChestStandardSoftPityThreshold,
                LuckDocuments.ShippedChestStandardSoftPitySlope))
            .ShouldBe(
                3.9,
                "24 §4.1: 'reaches roughly 4× base by chest 159'. misses = drawIndex − 1 = 158, so " +
                "1 + 0.05 × (158 − 100) = 3.9. Counting misses as the draw index would give 3.95 and " +
                "shift every soft-pity curve in the game by one draw.");
    }

    /// <summary>Each authored curve ramps on its own threshold and its own slope.</summary>
    /// <remarks>
    /// The negative control on the cases above, which all use one curve: a ramp that ignored its
    /// arguments and used the standard chest's numbers would pass every one of them.
    /// </remarks>
    [Theory]
    [InlineData(
        LuckDocuments.ShippedChestPremiumSoftPityThreshold,
        LuckDocuments.ShippedChestPremiumSoftPitySlope,
        24,
        1.72)]
    [InlineData(
        LuckDocuments.ShippedCrateMountSoftPityThreshold,
        LuckDocuments.ShippedCrateMountSoftPitySlope,
        29,
        1.9)]
    public void Every_authored_curve_ramps_on_its_own_threshold_and_slope(
        int threshold, double slope, int misses, double expected)
    {
        Rounded(SoftPity.WeightMultiplier(misses, threshold, slope)).ShouldBe(expected);
    }

    /// <summary>The ramp never drops below 1: a curve raises the target's odds, it never lowers them.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(500)]
    public void The_ramp_never_falls_below_one(int misses)
    {
        SoftPity.WeightMultiplier(misses, 100, 0.05).ShouldBeGreaterThanOrEqualTo(
            1.0,
            "24 §2: pity 'bounds the tail, it does not shift the mean' — downwards least of all.");
    }

    /// <summary>A threshold of zero ramps from the very first miss.</summary>
    /// <remarks>
    /// The schema permits <c>missThreshold: 0</c>, so the clamp has to be <c>max(0, …)</c> on the
    /// difference rather than a "below threshold" branch that skips the ramp entirely.
    /// </remarks>
    [Fact]
    public void A_threshold_of_zero_ramps_from_the_first_miss()
    {
        Rounded(SoftPity.WeightMultiplier(1, 0, 0.05)).ShouldBe(1.05);
    }

    /// <summary>Counts are never negative and a slope is a positive finite number.</summary>
    /// <remarks>
    /// Each case names the argument its refusal is about. All three of this curve's arguments carry
    /// a stated range, and a guard that reported the wrong one would satisfy a bare
    /// <c>Should.Throw</c> while leaving two ranges unchecked.
    /// </remarks>
    [Theory]
    [InlineData(-1, 100, 0.05, "misses")]
    [InlineData(0, -1, 0.05, "threshold")]
    [InlineData(0, 100, 0.0, "slope")]
    [InlineData(0, 100, -0.05, "slope")]
    [InlineData(0, 100, double.NaN, "slope")]
    [InlineData(0, 100, double.PositiveInfinity, "slope")]
    public void A_curve_outside_its_stated_ranges_is_refused(
        int misses, int threshold, double slope, string parameter)
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => SoftPity.WeightMultiplier(misses, threshold, slope))
            .ParamName.ShouldBe(parameter);
    }

    // ---------------------------------------------------------------- the ramp against a real table

    /// <summary>
    /// What the multiplier is <em>for</em>: it widens the target's band of the weighted walk by
    /// exactly its factor, and every other share shrinks proportionally.
    /// </summary>
    /// <remarks>
    /// The Mount Crate: <c>24</c> §4.5 authors both its odds (A 70 · S 26 · SS 4) and its curve
    /// (threshold 20, slope 0.10), so the multiplier at 29 misses is 1.9 and nothing here is
    /// invented. Asserted through <c>DeterministicRng.PickAt</c> at exact unit intervals rather than
    /// at a searched-for seed: the SS boundary moves from <c>96/100</c> to <c>96/103.6 = 0.92664</c>,
    /// and no seed can be searched for a value that close to it. <c>24</c> §4.0a rule 2: the
    /// multiplier applies to the target's weight <em>before</em> normalisation.
    /// </remarks>
    [Fact]
    public void The_ramp_widens_the_targets_band_of_the_walk_by_exactly_its_factor()
    {
        var multiplier = SoftPity.WeightMultiplier(
            29, LuckDocuments.ShippedCrateMountSoftPityThreshold, LuckDocuments.ShippedCrateMountSoftPitySlope);

        var walk = Walk(LuckTables.CrateMount());
        var ramped = Walk(LuckTables.CrateMount().Scale(Rarity.SS, multiplier));

        DeterministicRng.PickAt(0.9270, walk).ShouldBe(
            Rarity.S,
            "before the ramp the SS band is [96/100, 1), so 0.9270 lands in S");
        DeterministicRng.PickAt(0.9270, ramped).ShouldBe(
            Rarity.SS,
            "after ×1.9 the SS weight is 7.6 of 103.6, so the band opens at 96/103.6 = 0.92664 and " +
            "0.9270 is inside it. 24 §4.0a rule 2: the multiplier is applied before normalisation.");

        DeterministicRng.PickAt(0.9260, ramped).ShouldBe(
            Rarity.S,
            "0.9260 is below the new boundary — the ramp widens the band by exactly its factor, not " +
            "by more");

        DeterministicRng.PickAt(0.5, ramped).ShouldBe(
            DeterministicRng.PickAt(0.5, walk),
            "the ramp shrinks the other shares proportionally rather than reordering them, so a draw " +
            "far from the boundary lands in the same band either way");
    }

    // ---------------------------------------------------------------- the additive rate mercy
    //
    // The ramp, the ceiling and the ad boost run through the LuckService façade in
    // EnhanceMercyTests. What stays here is what the shipped block cannot express: a cap below 1,
    // and the primitive's own argument ranges.

    /// <summary>
    /// The cap is read from the argument, not assumed to be 1: a hard-coded <c>Math.Min(1.0, …)</c>
    /// passes every case using the shipped cap of <c>1.00</c> and silently ignores a block that
    /// authors a lower one.
    /// </summary>
    [Theory]
    [InlineData(9, 0.6)]
    [InlineData(2, 0.41)]
    public void A_cap_below_one_is_honoured_and_only_binds_once_the_ramp_reaches_it(
        int failures, double expected)
    {
        Rounded(SoftPity.RateWithMercy(EnhanceBaseRate, failures, EnhanceSlope, 0.6)).ShouldBe(expected);
    }

    /// <summary>Probabilities are in 0..1, counts are never negative, and a slope is positive and finite.</summary>
    /// <inheritdoc cref="A_curve_outside_its_stated_ranges_is_refused" path="/remarks"/>
    [Theory]
    [InlineData(-0.01, 0, 0.08, 1.0, "baseRate")]
    [InlineData(1.01, 0, 0.08, 1.0, "baseRate")]
    [InlineData(0.25, -1, 0.08, 1.0, "consecutiveFailures")]
    [InlineData(0.25, 0, 0.0, 1.0, "slope")]
    [InlineData(0.25, 0, -0.08, 1.0, "slope")]
    [InlineData(0.25, 0, double.NaN, 1.0, "slope")]
    [InlineData(0.25, 0, 0.08, -0.01, "cap")]
    [InlineData(0.25, 0, 0.08, 1.01, "cap")]
    public void A_mercy_rate_outside_its_stated_ranges_is_refused(
        double baseRate, int failures, double slope, double cap, string parameter)
    {
        Should.Throw<ArgumentOutOfRangeException>(
                () => SoftPity.RateWithMercy(baseRate, failures, slope, cap))
            .ParamName.ShouldBe(parameter);
    }

    /// <summary><c>08</c> §4.2's <c>+15</c> success rate, the base the mercy ramp is applied to.</summary>
    private const double EnhanceBaseRate = 0.25;

    /// <summary><c>24</c> §4.6's mercy slope, as <c>luck.json</c> authors it.</summary>
    private const double EnhanceSlope = LuckDocuments.ShippedEnhanceMercySlope;

    private static double Rounded(double value) => DeterminismRounding.Round(value);

    private static IReadOnlyList<(Rarity, double)> Walk(RarityTable table) =>
        table.Rows.Select(row => (row.Rarity, row.Weight)).ToArray();
}
