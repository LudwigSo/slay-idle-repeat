using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The inventory numbers: how many slots a player holds, how far that can grow, and what each step
/// of the growth costs.
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>Content/</c> rather than beside the container for <see cref="EnergyTuning"/>'s
/// reason: <c>Model</c> may not reference <c>Rules</c>, so the <c>Inventory</c> component and the
/// rules that read a stock both need these numbers from a layer beneath them both.
/// </para>
/// <para>
/// <b>It spans two documents, and the interesting validation is the one that crosses them.</b>
/// Capacity is a forge number and the ladder that moves it is a currency number, so the ceiling and
/// the ladder are authored in different files by different hands. The reader refuses a set where
/// they disagree — a ceiling no ladder can reach is a promise to the player that nothing can keep.
/// </para>
/// <para>
/// A hole is never a default: every read below goes through <see cref="ContentSnapshot"/>'s typed
/// readers, which throw <see cref="UnauthorisedTunableException"/> rather than answer zero.
/// </para>
/// <para>
/// Deliberately not read here: the rest of <c>tuning/forge.json</c> — merge, enhance, salvage,
/// reforge and retune are the forge task's and none of them is a capacity; the Crown and Soul Shard
/// <em>balances</em> (the wallet's, not this sink's); and every other sink under
/// <c>#/soulShards/sinks</c>, each of which belongs to the command that spends it.
/// </para>
/// </remarks>
internal sealed class InventoryTuning
{
    /// <summary>The document the capacity numbers live in.</summary>
    internal const string DocumentPath = "tuning/forge.json";

    /// <summary>The document the expansion prices live in.</summary>
    internal const string PricingDocumentPath = "tuning/currencies.json";

    private const string InventoryPointer = DocumentPath + "#/inventory";

    private const string CrownsPointer = PricingDocumentPath + "#/crowns";

    /// <summary>Slots a player holds before buying anything.</summary>
    internal const string BaseCapacityReference = InventoryPointer + "/baseCapacity";

    /// <summary>Slots one expansion adds, as the forge document states it.</summary>
    internal const string ExpansionStepReference = InventoryPointer + "/expansionStep";

    /// <summary>The ceiling capacity stops at. Derived, and checked against the ladder.</summary>
    internal const string MaxCapacityReference = InventoryPointer + "/maxCapacity";

    /// <summary>The escalating Crown price of each expansion, one rung per purchase.</summary>
    internal const string LadderReference = CrownsPointer + "/inventoryExpansionLadder";

    /// <summary>How many expansions can ever be bought.</summary>
    internal const string MaxPurchasesReference = CrownsPointer + "/inventoryExpansionMaxPurchases";

    /// <summary>Slots one expansion adds, as the currency document states it.</summary>
    internal const string SlotsPerPurchaseReference =
        CrownsPointer + "/inventoryExpansionSlotsPerPurchase";

    /// <summary>The flat Soul Shard price of one expansion, at any step of the ladder.</summary>
    internal const string FlatSoulShardReference =
        PricingDocumentPath + "#/soulShards/sinks/INVENTORY_EXPANSION_FLAT";

    /// <summary>
    /// 🔴 The bound on the overflow holding list — <b>absent</b>, because no document authors one.
    /// </summary>
    /// <remarks>
    /// A full inventory holds what it cannot store rather than dropping it, and nothing anywhere
    /// says how much it may hold, for how long, or what happens at the edge. Nullable so it cannot
    /// be read without handling the absence: a zero here would silently destroy the grants the hold
    /// rule exists to keep, and an <see cref="int.MaxValue"/> would be a number nobody chose
    /// wearing the costume of a decision. Filling it in is a design decision with an owner.
    /// </remarks>
    internal static readonly int? OverflowCapacity = null;

    private InventoryTuning(
        int baseCapacity,
        int expansionStep,
        int maxCapacity,
        IReadOnlyList<long> ladder,
        int maxPurchases,
        int slotsPerPurchase,
        long flatSoulShardPrice)
    {
        BaseCapacity = baseCapacity;
        ExpansionStep = expansionStep;
        MaxCapacity = maxCapacity;
        Ladder = ladder;
        MaxPurchases = maxPurchases;
        SlotsPerPurchase = slotsPerPurchase;
        FlatSoulShardPrice = flatSoulShardPrice;
    }

    /// <summary>Slots a player starts with. 120 as shipped.</summary>
    internal int BaseCapacity { get; }

    /// <summary>Slots one expansion adds. 20 as shipped, and equal to <see cref="SlotsPerPurchase"/> by rule.</summary>
    internal int ExpansionStep { get; }

    /// <summary>The ceiling capacity stops at. 320 as shipped — exactly what the ladder reaches.</summary>
    internal int MaxCapacity { get; }

    /// <summary>The Crown price of each expansion, in purchase order. Strictly ascending.</summary>
    internal IReadOnlyList<long> Ladder { get; }

    /// <summary>How many expansions can ever be bought. 10 as shipped.</summary>
    internal int MaxPurchases { get; }

    /// <summary>Slots one expansion adds, as the currency document states it. 20 as shipped.</summary>
    internal int SlotsPerPurchase { get; }

    /// <summary>
    /// The Soul Shard price of one expansion — one number for every step, not a ladder. 400 as
    /// shipped.
    /// </summary>
    /// <remarks>
    /// A property rather than a function of the purchase index, and that is the whole shape of the
    /// alternative: the Crown price escalates more than sevenfold across the ladder and this one
    /// does not move at all. A per-step accessor beside it would be the escalation arriving on the
    /// currency that is meant not to have one.
    /// </remarks>
    internal long FlatSoulShardPrice { get; }

    /// <summary>
    /// Capacity after <paramref name="expansionsPurchased"/> expansions: the base plus one step per
    /// purchase. 120, 140, … 320 as shipped.
    /// </summary>
    /// <param name="expansionsPurchased">
    /// Expansions already bought, from zero to <see cref="MaxPurchases"/> inclusive.
    /// </param>
    /// <returns>The capacity at that many purchases.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Outside <c>0..MaxPurchases</c>. Refused rather than clamped: a clamped answer past the cap
    /// would let a caller charge for a purchase that added nothing.
    /// </exception>
    internal int CapacityAt(int expansionsPurchased)
    {
        if (expansionsPurchased < 0 || expansionsPurchased > MaxPurchases)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expansionsPurchased),
                expansionsPurchased,
                "The expansion ladder runs from 0 to " + Render(MaxPurchases) + " purchases, so " +
                "there is no capacity outside it. Clamping would answer " + Render(MaxCapacity) +
                " for a purchase count nobody can reach and let a caller charge for slots it did " +
                "not add.");
        }

        return BaseCapacity + (expansionsPurchased * ExpansionStep);
    }

    /// <summary>The Crown price of the next expansion.</summary>
    /// <param name="nextPurchaseIndex">
    /// The number of expansions <b>already</b> bought, which is the index of the rung the next one
    /// costs. Zero is a brand-new player's first expansion.
    /// </param>
    /// <returns>That rung of the ladder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Outside <c>0..MaxPurchases-1</c>. Past the ladder there is no price, which is a different
    /// sentence from "there is no capacity" and is worth saying separately.
    /// </exception>
    internal long CrownPriceOf(int nextPurchaseIndex)
    {
        if (nextPurchaseIndex < 0 || nextPurchaseIndex >= Ladder.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextPurchaseIndex),
                nextPurchaseIndex,
                "The Crown ladder has " + Render(Ladder.Count) + " rungs, one per purchase, and " +
                "the index is the number of expansions ALREADY bought. There is no price outside " +
                "it — an eleventh expansion is refused before anybody is charged.");
        }

        return Ladder[nextPurchaseIndex];
    }

    /// <summary>
    /// Reads the capacity block and the expansion prices. Throws rather than defaulting on anything
    /// missing, unauthorised, mistyped or nonsensical.
    /// </summary>
    /// <param name="content">The version-stamped snapshot the command is reading.</param>
    /// <returns>The inventory numbers.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="MissingContentException">A document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A pointer holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static InventoryTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        // Every leaf is read before anything is judged, so a deliberate hole anywhere in the block
        // is reported as the hole it is rather than as whichever validation happened to run first.
        var baseCapacity = content.ReadInt32(BaseCapacityReference);
        var expansionStep = content.ReadInt32(ExpansionStepReference);
        var maxCapacity = content.ReadInt32(MaxCapacityReference);
        var maxPurchases = content.ReadInt32(MaxPurchasesReference);
        var slotsPerPurchase = content.ReadInt32(SlotsPerPurchaseReference);
        var flatSoulShardPrice = content.ReadInt64(FlatSoulShardReference);
        var ladder = ReadLadder(content);

        if (baseCapacity < 1)
        {
            throw new InvalidTunableException(
                BaseCapacityReference,
                "A player must start with at least one slot. An inventory holding nothing is not a " +
                "smaller inventory, it is a game with no gear in it. 08 §5 authors 120; this " +
                "document authors " + Render(baseCapacity) + ".");
        }

        if (expansionStep < 1)
        {
            throw new InvalidTunableException(
                ExpansionStepReference,
                "An expansion must add at least one slot, or the purchase buys nothing. 08 §5 " +
                "authors +20; this document authors " + Render(expansionStep) + ".");
        }

        if (maxPurchases < 1)
        {
            throw new InvalidTunableException(
                MaxPurchasesReference,
                "At least one expansion must be buyable, or the ladder, the ceiling and the sink " +
                "all describe a purchase nobody can make. 10 §4 authors 10; this document authors " +
                Render(maxPurchases) + ".");
        }

        if (slotsPerPurchase < 1)
        {
            throw new InvalidTunableException(
                SlotsPerPurchaseReference,
                "An expansion must add at least one slot. 10 §4 authors 20; this document authors " +
                Render(slotsPerPurchase) + ".");
        }

        if (expansionStep != slotsPerPurchase)
        {
            throw new InvalidTunableException(
                ExpansionStepReference,
                "The forge document's step (" + Render(expansionStep) + ") and the currency " +
                "document's slots-per-purchase (" + Render(slotsPerPurchase) + ") are the same " +
                "quantity written twice. When they disagree neither can be trusted: one of them " +
                "prices the purchase and the other decides what it buys.");
        }

        if (ladder.Count != maxPurchases)
        {
            throw new InvalidTunableException(
                LadderReference,
                "The ladder has " + Render(ladder.Count) + " rungs and " + Render(maxPurchases) +
                " expansions can be bought. Every purchase is priced by its own rung, so a shorter " +
                "ladder leaves expansions with no price and a longer one prices purchases nobody " +
                "can make.");
        }

        RequireStrictlyAscending(ladder);

        if (flatSoulShardPrice < 1)
        {
            throw new InvalidTunableException(
                FlatSoulShardReference,
                "A free expansion is not a sink. 10 §2 authors 400 Soul Shards as the flat " +
                "alternative to the Crown ladder; this document authors " +
                Render(flatSoulShardPrice) + ".");
        }

        var reachable = (long)baseCapacity + ((long)maxPurchases * slotsPerPurchase);
        if (maxCapacity != reachable)
        {
            throw new InvalidTunableException(
                MaxCapacityReference,
                "The ceiling is " + Render(maxCapacity) + " and the ladder reaches " +
                Render(reachable) + " (" + Render(baseCapacity) + " + " + Render(maxPurchases) +
                " x " + Render(slotsPerPurchase) + "). The ceiling is not an independent number: " +
                "above the ladder it promises slots no player can buy, and below it a purchase the " +
                "ladder prices would push the stock past a limit the game says exists.");
        }

        return new InventoryTuning(
            baseCapacity,
            expansionStep,
            maxCapacity,
            ladder,
            maxPurchases,
            slotsPerPurchase,
            flatSoulShardPrice);
    }

    /// <summary>The Crown ladder, as a read-only list of prices in purchase order.</summary>
    private static IReadOnlyList<long> ReadLadder(ContentSnapshot content)
    {
        var authored = content.Read(LadderReference);

        if (authored.IsUnauthorised)
        {
            throw new UnauthorisedTunableException(LadderReference);
        }

        if (authored.Kind != ContentValueKind.Array)
        {
            throw new ContentTypeMismatchException(
                LadderReference, authored.Kind, "an array of Crown prices, one rung per purchase");
        }

        var rungs = new long[authored.Items.Count];

        for (var rung = 0; rung < rungs.Length; rung++)
        {
            rungs[rung] = authored.Items[rung].AsInt64(
                LadderReference + "/" + Render(rung));
        }

        return Array.AsReadOnly(rungs);
    }

    /// <summary>
    /// The ladder escalates. A repeat is not an escalation and a fall makes the last slots the
    /// cheapest, so both are refused with the ladder's own pointer named.
    /// </summary>
    private static void RequireStrictlyAscending(IReadOnlyList<long> ladder)
    {
        if (ladder.Count > 0 && ladder[0] < 1)
        {
            throw new InvalidTunableException(
                LadderReference,
                "The first rung is " + Render(ladder[0]) + ". An expansion that costs nothing is " +
                "not a sink, and the ladder is the escalating one.");
        }

        for (var rung = 1; rung < ladder.Count; rung++)
        {
            if (ladder[rung] > ladder[rung - 1])
            {
                continue;
            }

            throw new InvalidTunableException(
                LadderReference,
                "Rung " + Render(rung) + " costs " + Render(ladder[rung]) + " and rung " +
                Render(rung - 1) + " costs " + Render(ladder[rung - 1]) + ". 10 §4's ladder rises " +
                "with every purchase, which is the whole shape of the sink: two expansions at one " +
                "price is a ladder that stopped escalating, and a falling one makes the last slots " +
                "the cheapest a player will ever buy.");
        }
    }

    /// <summary>
    /// Renders a number with <see cref="CultureInfo.InvariantCulture"/> — a bare interpolation
    /// would render a thousands separator on one host and not on another.
    /// </summary>
    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <inheritdoc cref="Render(int)"/>
    private static string Render(long value) => value.ToString(CultureInfo.InvariantCulture);
}
