using Shouldly;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The inventory numbers: what a player can hold, how far it can grow, and what each step costs.
/// The reader spans two documents — capacity is a forge number, the price of moving it a currency
/// number. Per the M4 retro's ruling of 2026-08-17 the ceiling is FLAT and the ladder is DEFERRED:
/// its reach must be strictly above the ceiling, so not one rung is buyable.
/// </summary>
public sealed class InventoryTuningTests
{
    /// <summary>The reader answers the authored numbers, whole.</summary>
    /// <remarks>
    /// The ladder is compared element by element: a reader that dropped a rung, sorted the list or
    /// read the wrong array would still answer ten ascending numbers.
    /// </remarks>
    [Fact]
    public void The_reader_answers_the_authored_capacity_and_the_authored_ladder()
    {
        var tuning = Read();

        tuning.BaseCapacity.ShouldBe(1000);
        tuning.ExpansionStep.ShouldBe(20);
        tuning.MaxCapacity.ShouldBe(
            1000, "the 2026-08-17 ruling caps capacity flat at 1000 — the ceiling IS the base");
        tuning.MaxPurchases.ShouldBe(10);
        tuning.SlotsPerPurchase.ShouldBe(20);
        tuning.FlatSoulShardPrice.ShouldBe(400L);

        tuning.Ladder.ShouldBe(
            new long[] { 800, 1000, 1250, 1560, 1950, 2440, 3050, 3810, 4770, 6000 });
    }

    /// <summary>
    /// 🔒 The ruling's two arms, told apart by the pointer each names: the deferred ladder must stay
    /// entirely out of reach, and the ceiling must be the base.
    /// </summary>
    /// <remarks>
    /// 1200 is not an arbitrary probe: it is exactly the shipped ladder's reach (1000 + 10 × 20), the
    /// pre-ruling state the old invariant demanded and this rule now refuses.
    /// </remarks>
    [Fact]
    public void The_deferred_ladder_must_stay_out_of_reach_and_the_ceiling_must_be_the_base()
    {
        // The ladder's reach, met exactly — the pre-ruling shape. It names the LADDER.
        Should.Throw<InvalidTunableException>(
                () => InventoryTuning.Read(
                    InventoryDocuments.With(maxCapacity: ContentValue.Number(1200))))
            .Reference.ShouldBe(
                InventoryTuning.LadderReference,
                "a ceiling ON the ladder's reach makes the last purchase buyable, which is the " +
                "state the 2026-08-17 ruling deferred.");

        // Past the reach: every rung buyable, not merely the last. Same arm, same pointer.
        Should.Throw<InvalidTunableException>(
                () => InventoryTuning.Read(
                    InventoryDocuments.With(maxCapacity: ContentValue.Number(5000))))
            .Reference.ShouldBe(InventoryTuning.LadderReference);

        // Inside the ladder's range but not the base: capacity is no longer flat. The OTHER arm,
        // and the other pointer — a value the ladder arm passes, so it discriminates.
        Should.Throw<InvalidTunableException>(
                () => InventoryTuning.Read(
                    InventoryDocuments.With(maxCapacity: ContentValue.Number(1100))))
            .Reference.ShouldBe(
                InventoryTuning.MaxCapacityReference,
                "1100 is below the ladder's 1200 reach, so the ladder arm is satisfied and only the " +
                "flatness arm can be firing. If this answers the ladder's pointer, the two arms have " +
                "collapsed into one.");

        // …and below the base, which would otherwise read as "merely conservative".
        Should.Throw<InvalidTunableException>(
                () => InventoryTuning.Read(
                    InventoryDocuments.With(maxCapacity: ContentValue.Number(900))))
            .Reference.ShouldBe(InventoryTuning.MaxCapacityReference);
    }

    /// <summary>An unusable number is refused, and the refusal names the leaf that was authored.</summary>
    /// <remarks>
    /// The rows name four different pointers between them — a reader that threw one shared refusal
    /// naming one shared pointer would pass rows that only asked "did it throw".
    /// </remarks>
    [Theory]
    [MemberData(nameof(UnusableNumbers))]
    public void An_unusable_number_is_refused_and_names_its_own_pointer(
        string reference, ContentSnapshot content, string why)
    {
        Should.Throw<InvalidTunableException>(() => InventoryTuning.Read(content), why)
            .Reference.ShouldBe(reference, why);
    }

    public static TheoryData<string, ContentSnapshot, string> UnusableNumbers() => new()
    {
        {
            InventoryTuning.BaseCapacityReference,
            InventoryDocuments.With(baseCapacity: ContentValue.Number(0)),
            "an inventory holding nothing is not a smaller inventory, it is a game with no gear in it"
        },
        {
            InventoryTuning.ExpansionStepReference,
            InventoryDocuments.With(expansionStep: ContentValue.Number(0)),
            "an expansion that adds no slots is a purchase that buys nothing"
        },
        {
            InventoryTuning.ExpansionStepReference,
            InventoryDocuments.With(expansionStep: ContentValue.Number(25)),
            "the forge's step and the currency document's slots-per-purchase are the same " +
            "quantity written twice, and when they disagree neither can be trusted"
        },
        {
            InventoryTuning.LadderReference,
            InventoryDocuments.With(ladder: InventoryDocuments.Ladder(800, 1000, 1250)),
            "three rungs and ten purchases means seven expansions with no price"
        },
        {
            InventoryTuning.LadderReference,
            InventoryDocuments.With(ladder: InventoryDocuments.Ladder(
                800, 1000, 1250, 1250, 1950, 2440, 3050, 3810, 4770, 6000)),
            "a repeated rung is not strictly ascending — the ladder stopped escalating"
        },
        {
            InventoryTuning.LadderReference,
            InventoryDocuments.With(ladder: InventoryDocuments.Ladder(
                6000, 4770, 3810, 3050, 2440, 1950, 1560, 1250, 1000, 800)),
            "a descending ladder makes the last slots the cheapest"
        },
        {
            InventoryTuning.FlatSoulShardReference,
            InventoryDocuments.With(flatSoulShardPrice: ContentValue.Number(0)),
            "a free expansion is not a sink"
        },
    };

    /// <summary>An eleventh expansion has no price on the deferred ladder.</summary>
    [Fact]
    public void There_is_no_price_past_the_last_rung_of_the_deferred_ladder()
    {
        var tuning = Read();

        // CrownPriceOf takes the index of the NEXT purchase, so index 9 is the tenth and last rung
        // and index 10 is the purchase that cannot be made.
        tuning.CrownPriceOf(9).ShouldBe(6000L);

        Should.Throw<ArgumentOutOfRangeException>(() => tuning.CrownPriceOf(10))
            .ParamName.ShouldBe("nextPurchaseIndex");

        Should.Throw<ArgumentOutOfRangeException>(() => tuning.CrownPriceOf(-1))
            .ParamName.ShouldBe("nextPurchaseIndex");
    }

    /// <summary>The Crown price of each purchase is that purchase's own rung, in order.</summary>
    [Fact]
    public void The_crown_price_of_a_purchase_is_its_own_rung_of_the_ladder()
    {
        var tuning = Read();

        Enumerable.Range(0, tuning.MaxPurchases).Select(tuning.CrownPriceOf).ShouldBe(tuning.Ladder);

        tuning.CrownPriceOf(0).ShouldBe(
            800L,
            "the first expansion is the cheapest, and the index is the number of expansions ALREADY " +
            "bought — an off-by-one here would charge a brand-new player the second rung.");
    }

    // ------------------------------------------------------------------------ the carried hole

    /// <summary>
    /// 🔴 No document authorises a bound on the overflow holding list, and asking for one
    /// <b>throws</b>, naming the pointer the value would be authored at — an invented zero would
    /// silently drop exactly the grants the hold rule exists to keep.
    /// </summary>
    [Fact]
    public void Asking_for_the_unauthored_bound_throws_and_names_its_pointer()
    {
        var refusal = Should.Throw<UnauthorisedTunableException>(
            () => InventoryTuning.RequireOverflowCapacity());

        refusal.Reference.ShouldBe(
            "tuning/forge.json#/inventory/overflowCapacity",
            "the pointer is the address the missing decision belongs at.");

        refusal.Reference.ShouldBe(InventoryTuning.OverflowCapacityReference);
    }

    // ---------------------------------------------------------------------------- the doors

    /// <summary>A missing document is a different failure from an unusable number.</summary>
    [Fact]
    public void A_missing_document_is_refused_as_a_missing_document()
    {
        Should.Throw<MissingContentException>(
            () => InventoryTuning.Read(InventoryDocuments.WithoutForge()));

        Should.Throw<MissingContentException>(
            () => InventoryTuning.Read(InventoryDocuments.WithoutCurrencies()));
    }

    /// <summary>A deliberate <c>null</c> is refused as an unauthorised hole, never read as a default.</summary>
    [Fact]
    public void A_deliberate_hole_is_refused_rather_than_read_as_a_default()
    {
        Should.Throw<UnauthorisedTunableException>(
            () => InventoryTuning.Read(
                InventoryDocuments.With(maxCapacity: ContentValue.Unauthorised)));

        Should.Throw<UnauthorisedTunableException>(
            () => InventoryTuning.Read(
                InventoryDocuments.With(flatSoulShardPrice: ContentValue.Unauthorised)));
    }

    /// <summary>A null content set is a caller defect, not a rejection.</summary>
    [Fact]
    public void A_null_content_set_is_refused()
    {
        Should.Throw<ArgumentNullException>(() => InventoryTuning.Read(null!))
            .ParamName.ShouldBe("content");
    }

    private static InventoryTuning Read() => InventoryTuning.Read(InventoryDocuments.Shipped);
}
