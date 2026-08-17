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
/// Capacity is a forge number; the ladder that <em>would</em> move it is a currency number. As of
/// the M4 retro's ruling of 2026-08-17 the ceiling is FLAT and the ladder is DEFERRED, so the
/// crossing rule is no longer "the two agree" but "the ladder stays out of reach": its reach must be
/// strictly above the ceiling, so not one rung is buyable. The pre-ruling documents met that reach
/// exactly, which is the state the rule now refuses.
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

        tuning.BaseCapacity.ShouldBe(1000);
        tuning.ExpansionStep.ShouldBe(20);
        tuning.MaxCapacity.ShouldBe(
            1000,
            "the M4 retro of 2026-08-17 ruled capacity flat at 1000 — 'virtually unlimited, cap it " +
            "by default at 1000 for now' — so the ceiling IS the base. It read 320 (120 + 10 × 20) " +
            "until then, which was M4 kickoff decision 4's derivation off a ladder nothing sells.");
        tuning.MaxPurchases.ShouldBe(10);
        tuning.SlotsPerPurchase.ShouldBe(20);
        tuning.FlatSoulShardPrice.ShouldBe(400L);

        tuning.Ladder.ShouldBe(
            new long[] { 800, 1000, 1250, 1560, 1950, 2440, 3050, 3810, 4770, 6000 });
    }

    /// <summary>
    /// 🔒 The ruling itself, in its two arms, <b>told apart by the pointer each names</b>: the
    /// deferred ladder must stay entirely out of reach, and the ceiling must be the base.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two arms are the whole replacement for the old "ceiling EQUALS the ladder's reach"
    /// equality, and they answer different pointers on purpose — a reader that threw one shared
    /// refusal would pass a case that only asked whether it threw (S2). The ladder arm names the
    /// LADDER, because a ceiling inside the ladder's range means somebody has decided expansions are
    /// spendable again and that is a `14` §2.3 vocabulary decision, not a capacity edit.
    /// </para>
    /// <para>
    /// 🔴 <b>1200 is not an arbitrary probe.</b> It is exactly what the shipped ladder reaches
    /// (<c>1000 + 10 × 20</c>) and therefore the exact state the pre-ruling documents were in: a
    /// ceiling equal to the reach. The rule that used to <em>demand</em> that equality now refuses
    /// it, so this probe fails on the old invariant's success case and nothing else.
    /// </para>
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

    /// <summary>Every other refusal, each naming its own pointer.</summary>
    /// <remarks>
    /// One case rather than seven, because the claim is that the seven are <em>distinguishable</em>:
    /// a reader that threw one shared exception naming one shared pointer would pass seven separate
    /// cases that each only asked "did it throw".
    /// <para>
    /// Each row moves <b>one</b> leaf. Several of them also disturb the flat-ceiling and
    /// out-of-reach arms — moving <c>baseCapacity</c> to zero leaves a ceiling that is no longer the
    /// base — so those rows are stated over the pointer whose <em>own</em> floor is meant to fire
    /// first, which is exactly the ordering claim.
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

    // ------------------------------------------------------- the flat ceiling and the dead curve

    /// <summary>
    /// 🔒 Capacity is one number, and there is no member that takes a purchase count to compute it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The structural half, said with reflection because no assertion over a <em>value</em> can see
    /// it: <c>CapacityAt(expansionsPurchased)</c> used to walk 120, 140, … 320, and re-adding it is
    /// exactly how a future caller would grow a stock past the flat ceiling the 2026-08-17 ruling
    /// set. The ladder itself is still read — see <see cref="The_crown_price_of_a_purchase_is_its_own_rung_of_the_ladder"/>
    /// — because the owner deferred the limit rather than deleting it, so "the growth is gone" cannot
    /// be stated as "the ladder is gone".
    /// </para>
    /// <para>
    /// The negative control is <c>CrownPriceOf</c>: it takes an <c>int</c> purchase index and must
    /// survive, so a rule spelled "no member takes a purchase index" would be wrong rather than
    /// merely weak. The claim is about the <em>capacity</em>, so it is stated over the name.
    /// </para>
    /// </remarks>
    [Fact]
    public void There_is_one_flat_capacity_and_no_member_derives_it_from_a_purchase_count()
    {
        var tuning = Read();

        tuning.MaxCapacity.ShouldBe(
            tuning.BaseCapacity,
            "the ceiling and the base are one number as of the 2026-08-17 ruling. If these ever " +
            "differ, Read's own flatness arm has stopped firing.");

        var members = typeof(InventoryTuning)
            .GetMembers(System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.DeclaredOnly)
            .Select(member => member.Name)
            .ToArray();

        members.ShouldContain(
            nameof(InventoryTuning.CrownPriceOf),
            "the negative control: the DEFERRED ladder is still read and still priced, so a sweep " +
            "that had lost every expansion member would satisfy the claim below for the wrong reason.");

        members.Where(name => name.Contains("CapacityAt", StringComparison.Ordinal)).ShouldBeEmpty(
            "capacity does not depend on a purchase count any more, so a member taking one would be " +
            "a function of an argument it has to ignore. MaxCapacity is the capacity.");
    }

    /// <summary>An eleventh expansion has no price on the deferred ladder, and the refusal says so.</summary>
    /// <remarks>
    /// It used to have no <em>capacity</em> either, through a <c>CapacityAt</c> that threw past the
    /// cap. Capacity no longer moves with purchases at all, so only the price half survives — and it
    /// still matters, because the ladder is authored for the day the owner deals with the limit.
    /// </remarks>
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
            // IsSpecialName drops property accessors: get_FlatSoulShardPrice IS an instance method
            // whose name contains the token, so without this the rule fires on the very property it
            // exists to protect and no shape could ever satisfy it.
            .Where(method => !method.IsSpecialName)
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

    /// <summary>
    /// 🔴 …and asking for the bound anyway <b>throws</b>, naming the pointer the value would be
    /// authored at.
    /// </summary>
    /// <remarks>
    /// The half the assertion above cannot make. A <c>null</c> field describes the hole to a reader
    /// and does nothing to a caller: <c>OverflowCapacity ?? 0</c> compiles, ships, and silently drops
    /// exactly the grants the hold rule exists to keep — the outcome the field's own remarks forbid,
    /// reached without editing a line of it. So the hole is enforced as well as described, and the
    /// reference is asserted rather than only the exception type: a throw that named nothing would
    /// tell a caller they may not have the number without telling them where the decision goes.
    /// </remarks>
    [Fact]
    public void Asking_for_the_unauthored_bound_throws_and_names_its_pointer()
    {
        var refusal = Should.Throw<UnauthorisedTunableException>(
            () => InventoryTuning.RequireOverflowCapacity());

        refusal.Reference.ShouldBe(
            "tuning/forge.json#/inventory/overflowCapacity",
            "the pointer is the address the missing decision belongs at, beside the capacity block " +
            "it would bound.");

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
