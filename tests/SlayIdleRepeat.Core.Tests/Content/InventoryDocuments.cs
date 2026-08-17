using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic fixtures for the two documents the inventory reader spans: <c>tuning/forge.json</c>'s
/// <c>inventory</c> block and <c>tuning/currencies.json</c>'s Crown ladder and flat Soul Shard sink.
/// </summary>
/// <remarks>
/// <para>
/// <c>Core.Tests</c> is hermetic, so this mirrors the shipped files rather than reading them — the
/// same shape <see cref="CurrenciesDocuments"/> and <see cref="GearDocuments"/> already use. An
/// <c>Application.Tests</c> rule pins the two halves together; neither is sufficient alone. This one
/// proves the reader is right about the numbers, that one proves those are the numbers we ship.
/// </para>
/// <para>
/// The reader spans two documents on purpose, and the fixture has to as well: capacity is a forge
/// number and the price of moving it is a currency number, so a fixture holding only one of them
/// could never exercise the cross-document agreement the reader's central validation is about.
/// </para>
/// </remarks>
internal static class InventoryDocuments
{
    /// <summary>Where the capacity numbers live.</summary>
    internal const string ForgeDocumentPath = "tuning/forge.json";

    /// <summary>Where the expansion prices live.</summary>
    internal const string CurrenciesDocumentPath = "tuning/currencies.json";

    /// <summary>Slots a player holds. Flat: nothing adds to it.</summary>
    internal const int ShippedBaseCapacity = 1000;

    /// <summary>
    /// Slots one expansion <em>would</em> add, as the forge document states it. Deferred and unspent.
    /// </summary>
    internal const int ShippedExpansionStep = 20;

    /// <summary>
    /// The ceiling, equal to <see cref="ShippedBaseCapacity"/> by rule as of the M4 retro's ruling of
    /// 2026-08-17. It used to be <c>120 + 10 × 20 = 320</c> — the ladder's reach — and is now an
    /// authored flat number the ladder must stay strictly above.
    /// </summary>
    internal const int ShippedMaxCapacity = 1000;

    /// <summary>How many expansions the deferred ladder prices. None of them is buyable.</summary>
    internal const int ShippedMaxPurchases = 10;

    /// <summary>Slots one expansion adds, as the currency document states it. Equal to the step by rule.</summary>
    internal const int ShippedSlotsPerPurchase = 20;

    /// <summary>The flat Soul Shard price of one expansion, at any step on the ladder.</summary>
    internal const long ShippedFlatSoulShardPrice = 400;

    /// <summary>The escalating Crown ladder, one rung per purchase.</summary>
    internal static IReadOnlyList<long> ShippedLadder { get; } =
        [800, 1000, 1250, 1560, 1950, 2440, 3050, 3810, 4770, 6000];

    /// <summary>Both documents, holding exactly the shipped inventory numbers.</summary>
    internal static ContentSnapshot Shipped { get; } = With();

    /// <summary>
    /// The shipped set with individual leaves replaced. Pass
    /// <see cref="ContentValue.Unauthorised"/> for a deliberate <c>null</c> hole, or omit a parameter
    /// to keep the shipped value.
    /// </summary>
    internal static ContentSnapshot With(
        ContentValue? baseCapacity = null,
        ContentValue? expansionStep = null,
        ContentValue? maxCapacity = null,
        ContentValue? ladder = null,
        ContentValue? maxPurchases = null,
        ContentValue? slotsPerPurchase = null,
        ContentValue? flatSoulShardPrice = null) =>
        new(
            ProgressionDocuments.Shipped.Version,
            [
                new ContentDocument(ForgeDocumentPath, Forge(baseCapacity, expansionStep, maxCapacity)),
                new ContentDocument(
                    CurrenciesDocumentPath,
                    Currencies(ladder, maxPurchases, slotsPerPurchase, flatSoulShardPrice)),
            ]);

    /// <summary>A ladder built from the given rungs, in the order given.</summary>
    /// <remarks>
    /// Takes the rungs rather than a count so a case can hand over an ascending ladder, a flat one and
    /// a descending one without three near-identical builders.
    /// </remarks>
    internal static ContentValue Ladder(params long[] rungs) =>
        ContentValue.Array(rungs.Select(rung => ContentValue.Number(rung)));

    /// <summary>The set with <b>no</b> <c>tuning/forge.json</c> at all — the missing-document door.</summary>
    internal static ContentSnapshot WithoutForge() =>
        new(
            ProgressionDocuments.Shipped.Version,
            [Shipped.GetDocument(CurrenciesDocumentPath)]);

    /// <summary>The set with <b>no</b> <c>tuning/currencies.json</c> at all.</summary>
    internal static ContentSnapshot WithoutCurrencies() =>
        new(ProgressionDocuments.Shipped.Version, [Shipped.GetDocument(ForgeDocumentPath)]);

    private static ContentValue Forge(
        ContentValue? baseCapacity, ContentValue? expansionStep, ContentValue? maxCapacity) =>
        ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["inventory"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                ["baseCapacity"] = baseCapacity ?? ContentValue.Number(ShippedBaseCapacity),
                ["expansionStep"] = expansionStep ?? ContentValue.Number(ShippedExpansionStep),
                ["maxCapacity"] = maxCapacity ?? ContentValue.Number(ShippedMaxCapacity),
            }),
        });

    private static ContentValue Currencies(
        ContentValue? ladder,
        ContentValue? maxPurchases,
        ContentValue? slotsPerPurchase,
        ContentValue? flatSoulShardPrice) =>
        ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["crowns"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                ["inventoryExpansionLadder"] = ladder ?? Ladder([.. ShippedLadder]),
                ["inventoryExpansionMaxPurchases"] =
                    maxPurchases ?? ContentValue.Number(ShippedMaxPurchases),
                ["inventoryExpansionSlotsPerPurchase"] =
                    slotsPerPurchase ?? ContentValue.Number(ShippedSlotsPerPurchase),
            }),
            ["soulShards"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                ["sinks"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                {
                    ["INVENTORY_EXPANSION_FLAT"] =
                        flatSoulShardPrice ?? ContentValue.Number(ShippedFlatSoulShardPrice),
                }),
            }),
        });
}
