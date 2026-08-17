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
/// 🔒 <b>Capacity is FLAT, and the ladder is deferred rather than deleted.</b> The M4 retro's
/// product-owner ruling of 2026-08-17 made the stock "virtually unlimited, cap it by default at
/// 1000 for now" and explicitly did <em>not</em> add an <c>EXPAND_INVENTORY</c> command to `14` §2.3's
/// vocabulary — so nothing can move the ceiling, and <see cref="MaxCapacity"/> equals
/// <see cref="BaseCapacity"/>. It supersedes M4 kickoff decision 4's <c>320 = 120 + 10 × 20</c>,
/// which was <c>[auto-accepted]</c> rather than ruled.
/// </para>
/// <para>
/// <b>It still spans two documents, and the validation that crosses them is now the deferral
/// itself.</b> The old invariant — ceiling <em>equals</em> the ladder's reach — had two sources to
/// reconcile and no longer does: the ceiling is an authored number rather than a derivation. What
/// replaces it is the claim that makes the deferral checkable, and it can still fail in both
/// directions: `10` §4's ladder must stay <b>authored and entirely out of reach</b> — its reach
/// (<c>baseCapacity + maxPurchases × slotsPerPurchase</c>) strictly above the ceiling, so not one
/// rung of it is buyable — and the ceiling must equal the base. Restore the pre-ruling numbers and
/// the first arm fires on the exact equality that used to be required; raise the ceiling into the
/// ladder's range and one of the two fires. The day a purchase command is authored, this is the
/// loud failure that says the block has to be re-ruled first.
/// </para>
/// <para>
/// ⚠️ <b>Said plainly, because it is the honest shape of the pair: the ladder arm's trigger set is a
/// strict SUBSET of the flatness arm's.</b> Every step size and purchase count is positive, so a
/// ceiling the ladder can reach is necessarily a ceiling above the base, and no document can trip
/// the first without also tripping the second. What the ladder arm buys is the <em>diagnosis</em>,
/// and that is why it is asked first: a reader told "the ceiling is not the base" would go and edit
/// the base, where what actually happened is that somebody made an expansion buyable. It also
/// outlives the flatness arm — the day capacity legitimately stops being flat, the flatness arm goes
/// and this one is the guard that remains.
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

    /// <summary>Slots a player holds. Flat: nothing buys more.</summary>
    internal const string BaseCapacityReference = InventoryPointer + "/baseCapacity";

    /// <summary>
    /// Slots one expansion <em>would</em> add, as the forge document states it. Deferred, and kept
    /// as the forge-side half of the one quantity both documents author.
    /// </summary>
    internal const string ExpansionStepReference = InventoryPointer + "/expansionStep";

    /// <summary>The ceiling capacity stops at. Authored flat, and checked against the deferred ladder.</summary>
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
    /// 🔴 Where a bound on the overflow holding list <b>would</b> be authored. Nothing is authored
    /// there, and this constant is the greppable name of that absence.
    /// </summary>
    /// <remarks>
    /// A pointer rather than a value, and a pointer that resolves to nothing on purpose: every other
    /// reference on this type addresses a number a designer wrote down, and this one addresses the
    /// place the missing decision belongs. Author a bound and it goes here, beside the capacity block
    /// it bounds.
    /// </remarks>
    internal const string OverflowCapacityReference = InventoryPointer + "/overflowCapacity";

    /// <summary>
    /// 🔴 The bound on the overflow holding list — <b>absent</b>, because no document authors one.
    /// </summary>
    /// <remarks>
    /// A full inventory holds what it cannot store rather than dropping it, and nothing anywhere
    /// says how much it may hold, for how long, or what happens at the edge. Nullable so it cannot
    /// be read without handling the absence: a zero here would silently destroy the grants the hold
    /// rule exists to keep, and an <see cref="int.MaxValue"/> would be a number nobody chose
    /// wearing the costume of a decision. Filling it in is a design decision with an owner.
    /// ⚠️ Nullability <em>describes</em> the hole and cannot enforce it —
    /// <c>OverflowCapacity ?? 0</c> compiles and produces exactly the silent destruction the
    /// paragraph above forbids. <see cref="RequireOverflowCapacity"/> is the half that fails loudly.
    /// </remarks>
    internal static readonly int? OverflowCapacity = null;

    /// <summary>
    /// 🔴 The bound on the holding list, <b>demanded</b> rather than defaulted. It always throws,
    /// because no document authors the value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The companion <see cref="OverflowCapacity"/> needs and cannot be. A nullable field states the
    /// absence to a reader; it does nothing to a caller who writes <c>?? 0</c>, <c>?? int.MaxValue</c>
    /// or <c>.GetValueOrDefault()</c> — each of which compiles, ships, and answers a number nobody
    /// chose. Anything that genuinely needs a bound calls this instead and stops the build's first
    /// run rather than the player's hundredth grant.
    /// </para>
    /// <para>
    /// <see cref="UnauthorisedTunableException"/> rather than a bespoke type: this is precisely the
    /// family's own case — a place the design set authorises no value — and its message already
    /// carries the reason ("It is not zero and it is not a default. Author the value, or do not read
    /// it."). The reference it names is <see cref="OverflowCapacityReference"/>, so the failure tells
    /// its reader where the decision goes as well as that it is missing.
    /// </para>
    /// </remarks>
    /// <returns>Never. The method exists to throw.</returns>
    /// <exception cref="UnauthorisedTunableException">Always — no bound on the holding list is authored.</exception>
    internal static int RequireOverflowCapacity() =>
        OverflowCapacity ?? throw new UnauthorisedTunableException(OverflowCapacityReference);

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

    /// <summary>Slots a player holds. 1000 as shipped, and the whole capacity — nothing adds to it.</summary>
    internal int BaseCapacity { get; }

    /// <summary>
    /// Slots one expansion would add. 20 as shipped, equal to <see cref="SlotsPerPurchase"/> by rule,
    /// and <b>unspent</b>: no command buys an expansion.
    /// </summary>
    internal int ExpansionStep { get; }

    /// <summary>
    /// The ceiling capacity stops at. 1000 as shipped, and equal to <see cref="BaseCapacity"/> by rule
    /// — capacity is flat, so this is the only capacity there is.
    /// </summary>
    internal int MaxCapacity { get; }

    /// <summary>
    /// The Crown price of each expansion, in purchase order. Strictly ascending, and <b>deferred</b>:
    /// authored, priced, and unreachable under the flat ceiling.
    /// </summary>
    internal IReadOnlyList<long> Ladder { get; }

    /// <summary>How many expansions the deferred ladder prices. 10 as shipped; none is buyable.</summary>
    internal int MaxPurchases { get; }

    /// <summary>Slots one expansion would add, as the currency document states it. 20 as shipped.</summary>
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

    // 🔒 There is deliberately no CapacityAt(expansionsPurchased) here any more. Capacity does not
    // depend on a purchase count under the 2026-08-17 ruling, so a member taking one would be a
    // function of an argument it has to ignore — and exactly the shape a future caller would reach
    // for to grow a stock past the flat ceiling. MaxCapacity IS the capacity. Its old body,
    // BaseCapacity + expansionsPurchased × ExpansionStep, is the arithmetic Read's
    // ladder-out-of-reach arm now refuses to let the data satisfy. The ladder itself is still read
    // and still priced — see CrownPriceOf — because the owner deferred the limit, not the ladder.

    /// <summary>The Crown price of the next expansion, on the deferred ladder nothing may buy.</summary>
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

        // 🔒 The two arms that replace the old "ceiling EQUALS the ladder's reach" equality, which
        // the 2026-08-17 ruling superseded. The ladder arm is asked FIRST on purpose: it is the one
        // that catches the pre-ruling document coming back, which is the failure a stale data set
        // actually produces, and it fails on precisely the equality the old rule demanded.
        var reachable = (long)baseCapacity + ((long)maxPurchases * slotsPerPurchase);

        if (reachable <= maxCapacity)
        {
            throw new InvalidTunableException(
                LadderReference,
                "The ladder reaches " + Render(reachable) + " (" + Render(baseCapacity) + " + " +
                Render(maxPurchases) + " x " + Render(slotsPerPurchase) + ") and the ceiling is " +
                Render(maxCapacity) + ", so a purchase this ladder prices would fit under it. It " +
                "must not. The 2026-08-17 retro ruled capacity FLAT and added no EXPAND_INVENTORY " +
                "command, so 10 §4's ladder is authored, priced and DEFERRED — every rung of it has " +
                "to buy slots the ceiling refuses, or the game carries a purchase it can neither " +
                "sell nor honour. Two ways to reach here: the pre-ruling derivation (120 + 10 x 20 " +
                "= 320, where the two met exactly) is back in the documents, or the ceiling was " +
                "raised into the ladder's range without anybody ruling on what may move it.");
        }

        if (maxCapacity != baseCapacity)
        {
            throw new InvalidTunableException(
                MaxCapacityReference,
                "The ceiling is " + Render(maxCapacity) + " and the base is " + Render(baseCapacity) +
                ". Capacity is flat as of the 2026-08-17 ruling — a player holds the base and no " +
                "command grows it — so a ceiling ABOVE the base promises slots nothing can buy, and " +
                "one BELOW it is a base no player may keep. The two are one number written twice.");
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
