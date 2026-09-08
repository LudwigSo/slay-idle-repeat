using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Gear;
using SlayIdleRepeat.Core.Rules.Inventory;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Inventory;

/// <summary>
/// The side-by-side delta: what a candidate item would change, stat by stat, against whatever is
/// worn in its slot.
/// </summary>
/// <remarks>
/// <para>
/// The Core seam only. Which item is equipped, and the arrows a screen draws from these numbers,
/// belong to the tasks that own the loadout and the forge screen; this answers the arithmetic they
/// both need and neither should reimplement.
/// </para>
/// <para>
/// 🔒 <b>The stats come from the same derivation the hero screen reads.</b> A second derivation here
/// would be a second answer to "what does this item give", and the two would eventually disagree by
/// a rounding step — which is precisely the difference a player would read as an upgrade.
/// </para>
/// </remarks>
public sealed class InventoryComparisonTests
{
    /// <summary>A comparison answers the two stats the item's slot authors, in derivation order.</summary>
    [Fact]
    public void A_comparison_answers_the_slots_primary_and_secondary_stats()
    {
        var candidate = Inventories.Item("candidate", rarity: Rarity.A, quality: 0.5);
        var coefficients = Inventories.Drops.Coefficients(candidate.Slot);

        var deltas = Compare(candidate, equipped: null);

        deltas.Select(delta => delta.Stat).ShouldBe(
            new[] { coefficients.PrimaryStat, coefficients.SecondaryStat },
            "primary then secondary, the order the derivation itself answers in.");
    }

    /// <summary>
    /// 🔒 With nothing equipped every stat is a pure gain: the equipped side is zero and the delta is
    /// the candidate's whole value.
    /// </summary>
    /// <remarks>
    /// Stated as three separate numbers rather than "the delta is positive". A comparison that
    /// returned the candidate's value as the <em>equipped</em> figure would also produce a positive
    /// delta of zero, and a screen would draw a green arrow beside no change at all.
    /// </remarks>
    [Fact]
    public void With_nothing_equipped_every_stat_is_a_pure_gain()
    {
        var candidate = Inventories.Item("candidate", rarity: Rarity.S, quality: 0.75);

        var deltas = Compare(candidate, equipped: null);

        deltas.ShouldNotBeEmpty();

        foreach (var delta in deltas)
        {
            delta.Equipped.ShouldBe(0.0, $"{delta.Stat}: nothing is worn, so there is nothing to lose");
            delta.Delta.ShouldBe(delta.Candidate, $"{delta.Stat}: the gain is the whole value");
            delta.Candidate.ShouldBeGreaterThan(
                0.0, $"{delta.Stat}: an S-band item at three-quarter quality gives something");
        }
    }

    /// <summary>The candidate's own figures are the derivation's, not a second computation.</summary>
    /// <remarks>
    /// Compared against <c>GearStatDerivation</c> directly, so a comparison that folded in the
    /// enhancement level, or rounded once more, is a difference this case can see.
    /// </remarks>
    [Fact]
    public void The_candidates_figures_are_the_derivations_own()
    {
        var candidate = Inventories.Item("candidate", rarity: Rarity.A, quality: 0.25);

        var primary = GearStatDerivation.Primary(Inventories.Par, Inventories.Drops, candidate);
        var secondary = GearStatDerivation.Secondary(Inventories.Par, Inventories.Drops, candidate);

        var deltas = Compare(candidate, equipped: null);

        deltas[0].Candidate.ShouldBe(primary.Value);
        deltas[0].IsPercent.ShouldBe(primary.IsPercent);
        deltas[1].Candidate.ShouldBe(secondary.Value);
        deltas[1].IsPercent.ShouldBe(
            secondary.IsPercent,
            "a percent stat feeds a capped percentage and a flat one does not; a screen that added " +
            "the two would show a number nothing in the game computes.");
    }

    /// <summary>A better candidate reads as a gain and a worse one as a loss, per stat.</summary>
    /// <remarks>
    /// Both directions in one case: a delta computed the wrong way round is right about the magnitude
    /// every time and wrong about the sign every time, and a suite that only ever compared an upgrade
    /// would never notice.
    /// </remarks>
    [Fact]
    public void A_stronger_candidate_gains_and_a_weaker_one_loses()
    {
        var worn = Inventories.Item("worn", rarity: Rarity.A, quality: 0.5);
        var better = Inventories.Item("better", rarity: Rarity.S, quality: 0.5);
        var worse = Inventories.Item("worse", rarity: Rarity.B, quality: 0.5);

        var gains = Compare(better, worn);
        var losses = Compare(worse, worn);

        gains.ShouldNotBeEmpty("the assertions below live inside a loop (steering S3)");
        losses.ShouldNotBeEmpty("the assertions below live inside a loop (steering S3)");

        foreach (var delta in gains)
        {
            delta.Delta.ShouldBeGreaterThan(0.0, $"{delta.Stat}: an S band beats an A band");
            delta.Equipped.ShouldBeGreaterThan(0.0, $"{delta.Stat}: something is worn");
        }

        foreach (var delta in losses)
        {
            delta.Delta.ShouldBeLessThan(0.0, $"{delta.Stat}: a B band loses to an A band");
        }
    }

    /// <summary>An identical item is a delta of exactly zero, not a small number.</summary>
    /// <remarks>
    /// The case that fails if the two sides are derived at different rounding, or if one of them
    /// silently reads a different chapter of origin.
    /// </remarks>
    [Fact]
    public void An_identical_item_is_a_delta_of_zero()
    {
        var worn = Inventories.Item("worn", rarity: Rarity.A, quality: 0.5, chapterOrigin: 4);
        var same = Inventories.Item("same", rarity: Rarity.A, quality: 0.5, chapterOrigin: 4);

        var deltas = Compare(same, worn);

        deltas.ShouldNotBeEmpty(
            "ShouldAllBe over an empty sequence is satisfied by a comparison that answers nothing " +
            "(steering S3).");
        deltas.ShouldAllBe(delta => delta.Delta == 0.0);
    }

    /// <summary>Every figure a comparison hands out is rounded to the assembly's determinism precision.</summary>
    /// <remarks>
    /// A delta is a subtraction of two rounded numbers, which is not itself guaranteed to be rounded
    /// — and this number reaches a screen, a wire response and, through them, a comparison between a
    /// client's arithmetic and the server's.
    /// </remarks>
    [Fact]
    public void Every_figure_is_rounded()
    {
        var deltas = Compare(
            Inventories.Item("candidate", rarity: Rarity.S, quality: 0.3333, chapterOrigin: 7),
            Inventories.Item("worn", rarity: Rarity.A, quality: 0.6667, chapterOrigin: 3));

        deltas.ShouldNotBeEmpty("the assertions below live inside a loop (steering S3)");

        foreach (var delta in deltas)
        {
            DeterminismRounding.IsRounded(delta.Candidate).ShouldBeTrue(delta.Stat);
            DeterminismRounding.IsRounded(delta.Equipped).ShouldBeTrue(delta.Stat);
            DeterminismRounding.IsRounded(delta.Delta).ShouldBeTrue(delta.Stat);
        }
    }

    /// <summary>Comparing across slots is a caller defect — the two items author different stats.</summary>
    /// <remarks>
    /// A boot and a blade have no stat in common to subtract, so the "delta" would be each item's own
    /// value reported as a gain and a loss of two unrelated quantities. The screen compares against
    /// what is worn <em>in that slot</em>; reaching here with anything else is a miswired caller.
    /// </remarks>
    [Fact]
    public void Comparing_across_slots_is_refused()
    {
        var blade = Inventories.Item("blade", GearFamily.BLADE);
        var treads = Inventories.Item("treads", GearFamily.TREADS);

        blade.Slot.ShouldNotBe(treads.Slot, "the fixture has to actually straddle two slots");

        Should.Throw<ArgumentException>(() => Compare(blade, treads))
            .ParamName.ShouldBe("equipped");
    }

    /// <summary>Every reference argument is required, and each refusal names its own.</summary>
    [Fact]
    public void A_null_argument_is_refused()
    {
        var candidate = Inventories.Item("candidate");

        Should.Throw<ArgumentNullException>(() => InventoryComparison.Compare(
                null!, Inventories.Drops, Inventories.Forge, candidate, null))
            .ParamName.ShouldBe("par");

        Should.Throw<ArgumentNullException>(() => InventoryComparison.Compare(
                Inventories.Par, null!, Inventories.Forge, candidate, null))
            .ParamName.ShouldBe("drops");

        Should.Throw<ArgumentNullException>(() => InventoryComparison.Compare(
                Inventories.Par, Inventories.Drops, null!, candidate, null))
            .ParamName.ShouldBe("forge");

        Should.Throw<ArgumentNullException>(() => InventoryComparison.Compare(
                Inventories.Par, Inventories.Drops, Inventories.Forge, null!, null))
            .ParamName.ShouldBe("candidate");
    }

    // `08` §4.2: +1 is a 7 % step, so an enhanced twin of the worn item is a gain on every stat — the
    // raw derivation calls the two a tie, which is the answer this case exists to refuse.
    [Fact]
    public void An_enhanced_twin_of_the_worn_item_gains_by_the_forge_multiplier()
    {
        var worn = Inventories.Item("worn", rarity: Rarity.A, quality: 0.5);
        var enhanced = Inventories.Item("enhanced", rarity: Rarity.A, quality: 0.5, enhanceLevel: 5);

        var deltas = Compare(enhanced, worn);

        deltas.ShouldNotBeEmpty("the assertions below live inside a loop (steering S3)");

        foreach (var delta in deltas)
        {
            delta.Delta.ShouldBeGreaterThan(0.0, $"{delta.Stat}: +5 beats +0 on the same item");
            delta.Candidate.ShouldBe(
                DeterminismRounding.Round(delta.Equipped * Inventories.Forge.StatMultiplier(5)),
                $"{delta.Stat}: the candidate is the worn figure under the +5 multiplier, and nothing else");
        }
    }

    private static IReadOnlyList<GearStatDelta> Compare(
        Core.Model.Gear.GearInstance candidate, Core.Model.Gear.GearInstance? equipped) =>
        InventoryComparison.Compare(
            Inventories.Par, Inventories.Drops, Inventories.Forge, candidate, equipped);
}
