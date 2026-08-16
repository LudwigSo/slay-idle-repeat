using Shouldly;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// The inventory numbers: what a player can hold, how far it can grow, and what each step costs.
/// </summary>
/// <remarks>
/// <para>
/// <b>The reader spans two documents, and the interesting rule is the one that crosses them.</b>
/// Capacity is a forge number; the ladder that moves it is a currency number. The two used to
/// disagree — one document authored a ceiling the other's ladder could never reach — and the whole
/// point of reading them together is that the disagreement is a refusal at load time rather than a
/// ceiling nobody can buy their way to.
/// </para>
/// <para>
/// Hermetic: every case here runs over <see cref="InventoryDocuments"/>, which mirrors the shipped
/// files. That the shipped files still say these numbers is <c>Application.Tests</c>' claim, not this
/// suite's.
/// </para>
/// </remarks>
public sealed class InventoryTuningTests
{
    /// <summary>The reader's pointers are the ones the documents author.</summary>
    /// <remarks>
    /// Pinned as strings rather than exercised only through <c>Read</c>: a renamed pointer that
    /// happened to resolve somewhere else would read a number nobody authored for it.
    /// </remarks>
    [Fact]
    public void The_pointers_the_reader_reads_are_the_documented_ones()
    {
        InventoryTuning.DocumentPath.ShouldBe("tuning/forge.json");

        InventoryTuning.BaseCapacityReference.ShouldBe("tuning/forge.json#/inventory/baseCapacity");
        InventoryTuning.ExpansionStepReference.ShouldBe("tuning/forge.json#/inventory/expansionStep");
        InventoryTuning.MaxCapacityReference.ShouldBe("tuning/forge.json#/inventory/maxCapacity");

        InventoryTuning.LadderReference.ShouldBe(
            "tuning/currencies.json#/crowns/inventoryExpansionLadder");
        InventoryTuning.MaxPurchasesReference.ShouldBe(
            "tuning/currencies.json#/crowns/inventoryExpansionMaxPurchases");
        InventoryTuning.SlotsPerPurchaseReference.ShouldBe(
            "tuning/currencies.json#/crowns/inventoryExpansionSlotsPerPurchase");
        InventoryTuning.FlatSoulShardReference.ShouldBe(
            "tuning/currencies.json#/soulShards/sinks/INVENTORY_EXPANSION_FLAT");
    }

    /// <summary>The reader answers the authored numbers, whole.</summary>
    /// <remarks>
    /// The ladder is compared element by element against a literal rather than against a length or a
    /// first rung: a reader that dropped a rung, sorted the list or read the wrong array would still
    /// answer ten ascending numbers.
    /// </remarks>
    [Fact]
    public void The_reader_answers_the_authored_capacity_and_the_authored_ladder()
    {
        var tuning = Read();

        tuning.BaseCapacity.ShouldBe(120);
        tuning.ExpansionStep.ShouldBe(20);
        tuning.MaxCapacity.ShouldBe(
            320,
            "120 + 10 purchases of +20. The forge document carried 400 until this task, which is a " +
            "ceiling no player could ever buy their way to.");
        tuning.MaxPurchases.ShouldBe(10);
        tuning.SlotsPerPurchase.ShouldBe(20);
        tuning.FlatSoulShardPrice.ShouldBe(400L);

        tuning.Ladder.ShouldBe(
            new long[] { 800, 1000, 1250, 1560, 1950, 2440, 3050, 3810, 4770, 6000 });
    }

    /// <summary>
    /// 🔒 The ruling itself: the ceiling must be exactly what the ladder reaches, and a document that
    /// says otherwise is refused with the ceiling's own pointer named.
    /// </summary>
    /// <remarks>
    /// 400 is not an arbitrary probe — it is the number the forge document actually carried, and the
    /// number a future edit would most plausibly restore. The reference in the refusal is the
    /// discriminating part: a reader that threw naming the ladder instead would be telling whoever
    /// reads the crash to edit the prices.
    /// </remarks>
    [Fact]
    public void A_ceiling_the_ladder_cannot_reach_is_refused_and_names_the_ceiling()
    {
        Should.Throw<InvalidTunableException>(
                () => InventoryTuning.Read(
                    InventoryDocuments.With(maxCapacity: ContentValue.Number(400))))
            .Reference.ShouldBe(InventoryTuning.MaxCapacityReference);

        // The other side of the same equation: a ceiling BELOW what the ladder reaches is the same
        // fault, and would otherwise read as "merely conservative".
        Should.Throw<InvalidTunableException>(
                () => InventoryTuning.Read(
                    InventoryDocuments.With(maxCapacity: ContentValue.Number(300))))
            .Reference.ShouldBe(InventoryTuning.MaxCapacityReference);
    }

    /// <summary>Every other refusal, each naming its own pointer.</summary>
    /// <remarks>
    /// One case rather than seven, because the claim is that the seven are <em>distinguishable</em>:
    /// a reader that threw one shared exception naming one shared pointer would pass seven separate
    /// cases that each only asked "did it throw".
    /// <para>
    /// Each row moves <b>one</b> leaf and keeps the ceiling equation satisfiable where it can be. The
    /// two capacity floors and the flat-price floor cannot — moving <c>baseCapacity</c> to zero also
    /// breaks the ceiling equation — so those rows are stated over the pointer whose <em>own</em>
    /// floor is meant to fire first, which is exactly the ordering claim.
    /// </para>
    /// </remarks>
    [Fact]
    public void An_unusable_number_is_refused_and_names_its_own_pointer()
    {
        var rows = new (string Reference, ContentSnapshot Content, string Why)[]
        {
            (InventoryTuning.BaseCapacityReference,
                InventoryDocuments.With(baseCapacity: ContentValue.Number(0)),
                "an inventory holding nothing is not a smaller inventory, it is a game with no gear in it"),

            (InventoryTuning.ExpansionStepReference,
                InventoryDocuments.With(expansionStep: ContentValue.Number(0)),
                "an expansion that adds no slots is a purchase that buys nothing"),

            (InventoryTuning.ExpansionStepReference,
                InventoryDocuments.With(expansionStep: ContentValue.Number(25)),
                "the forge's step and the currency document's slots-per-purchase are the same " +
                "quantity written twice, and when they disagree neither can be trusted"),

            (InventoryTuning.LadderReference,
                InventoryDocuments.With(ladder: InventoryDocuments.Ladder(800, 1000, 1250)),
                "three rungs and ten purchases means seven expansions with no price"),

            (InventoryTuning.LadderReference,
                InventoryDocuments.With(ladder: InventoryDocuments.Ladder(
                    800, 1000, 1250, 1250, 1950, 2440, 3050, 3810, 4770, 6000)),
                "a repeated rung is not strictly ascending — two expansions at one price is a " +
                "ladder that stopped escalating, which is the sink's whole shape"),

            (InventoryTuning.LadderReference,
                InventoryDocuments.With(ladder: InventoryDocuments.Ladder(
                    6000, 4770, 3810, 3050, 2440, 1950, 1560, 1250, 1000, 800)),
                "a descending ladder makes the last slots the cheapest"),

            (InventoryTuning.FlatSoulShardReference,
                InventoryDocuments.With(flatSoulShardPrice: ContentValue.Number(0)),
                "a free expansion is not a sink"),
        };

        foreach (var (reference, content, why) in rows)
        {
            Should.Throw<InvalidTunableException>(() => InventoryTuning.Read(content), why)
                .Reference.ShouldBe(reference, why);
        }

        rows.Select(row => row.Reference).Distinct(StringComparer.Ordinal).Count().ShouldBe(
            4,
            "the seven rows name four different pointers between them. A reader that threw one " +
            "shared refusal naming one shared pointer would pass seven cases that each only asked " +
            "whether it threw.");
    }

    // ------------------------------------------------------------------- the expansion curve

    /// <summary>Capacity at every purchase count the ladder authorises: 120, 140, … 320.</summary>
    /// <remarks>
    /// Written as the whole sequence against a literal, not as a spot check at either end. A reader
    /// that added the step twice per purchase, or clamped early, agrees with 120 and disagrees only
    /// in the middle.
    /// </remarks>
    [Fact]
    public void Capacity_walks_the_authored_step_from_the_base_to_the_ceiling()
    {
        var tuning = Read();

        Enumerable.Range(0, 11).Select(tuning.CapacityAt).ShouldBe(
            new[] { 120, 140, 160, 180, 200, 220, 240, 260, 280, 300, 320 });

        tuning.CapacityAt(tuning.MaxPurchases).ShouldBe(
            tuning.MaxCapacity,
            "the last purchase lands exactly on the ceiling — that agreement IS the ruling, and it " +
            "would be worth stating even if the sequence above did not already show it.");
    }

    /// <summary>An eleventh expansion has no capacity and no price, and both refusals say so.</summary>
    /// <remarks>
    /// Two separate refusals rather than one: "there is no capacity past the cap" and "there is no
    /// price past the ladder" are different sentences to whoever reads the crash, and a caller that
    /// got a clamped 320 back from the first would go on to charge for a purchase that added nothing.
    /// </remarks>
    [Fact]
    public void There_is_no_capacity_and_no_price_past_the_last_purchase()
    {
        var tuning = Read();

        Should.Throw<ArgumentOutOfRangeException>(() => tuning.CapacityAt(11))
            .ParamName.ShouldBe("expansionsPurchased");

        Should.Throw<ArgumentOutOfRangeException>(() => tuning.CapacityAt(-1))
            .ParamName.ShouldBe("expansionsPurchased");

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

    /// <summary>The Soul Shard alternative is flat: the same price at every step of the ladder.</summary>
    /// <remarks>
    /// The claim is the contrast, so it is stated against the ladder rather than as a constant: the
    /// last Crown rung is more than seven times the first, and the Soul Shard price does not move at
    /// all. A reader that scaled the shard price with the step would still answer 400 at index 0.
    /// </remarks>
    [Fact]
    public void The_soul_shard_price_does_not_move_along_the_ladder()
    {
        var tuning = Read();

        tuning.FlatSoulShardPrice.ShouldBe(400L);

        tuning.CrownPriceOf(tuning.MaxPurchases - 1).ShouldBeGreaterThan(
            tuning.CrownPriceOf(0) * 7,
            "the Crown ladder escalates steeply, which is what makes the flat shard price the " +
            "alternative it is meant to be.");

        // 🔒 "Flat" said structurally, because a property cannot be asserted to be constant across a
        // step it does not take: the Crown price is a FUNCTION of the purchase index and the shard
        // price is a PROPERTY. A per-step shard accessor appearing beside it would be the escalation
        // arriving on the currency that is meant not to have one, and no assertion over the value
        // could see that.
        typeof(InventoryTuning)
            .GetMethods(System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => method.Name.Contains("SoulShard", StringComparison.Ordinal))
            .ShouldBeEmpty(
                "the flat price is one number for every step, so it is a property and there is no " +
                "SoulShardPriceOf(k) beside CrownPriceOf(k). If one is ever wanted, the price stopped " +
                "being flat and 10 §2 has to say so first.");

        typeof(InventoryTuning)
            .GetProperty(nameof(InventoryTuning.FlatSoulShardPrice),
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic)
            .ShouldNotBeNull("…and the one number is exposed as exactly that.");
    }

    // ------------------------------------------------------------------------ the carried hole

    /// <summary>
    /// 🔴 Nothing authorises a bound on the overflow holding list, and the reader carries that as a
    /// hole rather than inventing one.
    /// </summary>
    /// <remarks>
    /// Nullable so it cannot be read without handling the absence: a zero or an <c>int.MaxValue</c>
    /// here would be a number nobody chose, and the first would silently drop items the hold rule
    /// exists to keep.
    /// </remarks>
    [Fact]
    public void No_document_authorises_a_bound_on_the_holding_list()
    {
        InventoryTuning.OverflowCapacity.ShouldBeNull(
            "a bound on the holding list would be a cap on how much a player can be handed while " +
            "their stock is full, and no document authors one. Filling this in is a design decision " +
            "with an owner, not a default.");
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
