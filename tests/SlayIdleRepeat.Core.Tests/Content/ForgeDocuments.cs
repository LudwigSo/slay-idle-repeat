using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic <c>tuning/forge.json</c> fixtures for the three free Forge operations — the
/// <c>merge</c>, <c>enhance</c> and <c>salvage</c> blocks <see cref="ForgeTuning"/> reads.
/// </summary>
/// <remarks>
/// <para>
/// <c>Core.Tests</c> is hermetic, so this mirrors the shipped file rather than reading it; an
/// <c>Application.Tests</c> rule pins the two halves together by resolving the same JSON pointers in
/// the real file. Neither is sufficient alone: this proves the rules are right about the numbers,
/// that one proves those are the numbers we ship.
/// </para>
/// <para>
/// Only what a reader reads is transcribed. The <c>reforge</c> and <c>retune</c> blocks are absent
/// because nothing reads them yet, and authoring them here would imply something does. The
/// <c>inventory</c> block <em>is</em> here, because the shipped document is one document: a forge
/// command that removes an item hands <c>InventoryTuning</c> to the container for the reclaim, and
/// two fixtures cannot occupy one path. Its numbers are read from
/// <see cref="InventoryDocuments"/> rather than transcribed again.
/// </para>
/// <para>
/// 🔒 The top band's dust price is an authored <c>null</c> rather than an omitted row, and the two
/// are different things: nothing merges out of the top rung, so the document states the absence.
/// A fixture that omitted it could not tell the reader's two refusals apart.
/// </para>
/// </remarks>
internal static class ForgeDocuments
{
    /// <summary>Where the three blocks live.</summary>
    internal const string DocumentPath = "tuning/forge.json";

    /// <summary>How many items one fusion consumes.</summary>
    internal const int ShippedMergeInputCount = 3;

    /// <summary>How many of those slots Merge Dust may fill.</summary>
    internal const int ShippedDustSubstituteMaxInputs = 1;

    /// <summary>The enhancement level a fresh item sits at.</summary>
    internal const int ShippedMinEnhanceLevel = 0;

    /// <summary>The enhancement level nothing goes past.</summary>
    internal const int ShippedMaxEnhanceLevel = 15;

    /// <summary>The additive share of base stats one level adds.</summary>
    internal const double ShippedStatBonusPerLevel = 0.07;

    /// <summary>The multiplier the ceiling is worth: <c>1 + 0.07 × 15</c>.</summary>
    internal const double ShippedTotalMultiplierAtMax = 2.05;

    /// <summary>The share of the base dust each enhancement level adds back on salvage.</summary>
    internal const double ShippedDustPerEnhanceLevel = 0.15;

    /// <summary>The share of the stones an item's level cost that salvage returns.</summary>
    internal const double ShippedStoneRefundShare = 0.6;

    /// <summary>The stone price of each attempt, from <c>+1</c> to the ceiling.</summary>
    internal static IReadOnlyList<long> ShippedStoneCosts { get; } =
        [2, 3, 4, 6, 8, 12, 16, 22, 30, 40, 55, 75, 100, 140, 200];

    /// <summary>
    /// The unmodified chance of each attempt, from <c>+1</c> to the ceiling — the band endpoints
    /// spread linearly and evenly across each band's five levels.
    /// </summary>
    internal static IReadOnlyList<double> ShippedSuccessRates { get; } =
    [
        1.0, 1.0, 1.0, 1.0, 1.0,
        0.85, 0.8, 0.75, 0.7, 0.65,
        0.5, 0.4375, 0.375, 0.3125, 0.25,
    ];

    /// <summary>The Merge Dust price of a substituted slot, keyed on the input band. The top rung is an authored null.</summary>
    internal static IReadOnlyList<(string Band, long? Price)> ShippedDustSubstituteCosts { get; } =
        [("C", 50), ("B", 200), ("A", 800), ("S", 3200), ("SS", null)];

    /// <summary>The Crown price of a fusion, keyed on the OUTPUT band — so there is deliberately no bottom-band row.</summary>
    internal static IReadOnlyList<(string Band, long? Price)> ShippedCrownCosts { get; } =
        [("B", 120), ("A", 600), ("S", 3000), ("SS", 15000)];

    /// <summary>The dust an unenhanced item of each band breaks down into.</summary>
    internal static IReadOnlyList<(string Band, long? Value)> ShippedBaseDust { get; } =
        [("C", 10), ("B", 40), ("A", 160), ("S", 640), ("SS", 2560)];

    /// <summary>A content set holding exactly the shipped forge numbers.</summary>
    internal static ContentSnapshot Shipped { get; } = With();

    /// <summary>
    /// The shipped set with individual leaves replaced. Pass
    /// <see cref="ContentValue.Unauthorised"/> for a deliberate <c>null</c> hole, or omit a parameter
    /// to keep the shipped value.
    /// </summary>
    /// <param name="inputCount">How many items one fusion consumes.</param>
    /// <param name="dustSubstituteMaxInputs">How many slots dust may fill.</param>
    /// <param name="dustSubstituteCost">The whole input-band-keyed dust price map.</param>
    /// <param name="crownCost">The whole output-band-keyed Crown price map.</param>
    /// <param name="minLevel">The enhancement floor.</param>
    /// <param name="maxLevel">The enhancement ceiling.</param>
    /// <param name="statBonusPerLevel">The additive share one level adds.</param>
    /// <param name="stoneCostPerLevel">The whole stone-cost ladder.</param>
    /// <param name="perLevelSuccessRate">The whole success-chance ladder.</param>
    /// <param name="baseDustByRarity">The whole band-keyed salvage value map.</param>
    /// <param name="dustPerEnhanceLevel">The per-level salvage bonus share.</param>
    /// <param name="stoneRefundShare">The salvage refund share.</param>
    internal static ContentSnapshot With(
        ContentValue? inputCount = null,
        ContentValue? dustSubstituteMaxInputs = null,
        ContentValue? dustSubstituteCost = null,
        ContentValue? crownCost = null,
        ContentValue? minLevel = null,
        ContentValue? maxLevel = null,
        ContentValue? statBonusPerLevel = null,
        ContentValue? stoneCostPerLevel = null,
        ContentValue? perLevelSuccessRate = null,
        ContentValue? baseDustByRarity = null,
        ContentValue? dustPerEnhanceLevel = null,
        ContentValue? stoneRefundShare = null) =>
        new(
            ProgressionDocuments.Shipped.Version,
            [
                Document(
                    inputCount,
                    dustSubstituteMaxInputs,
                    dustSubstituteCost,
                    crownCost,
                    minLevel,
                    maxLevel,
                    statBonusPerLevel,
                    stoneCostPerLevel,
                    perLevelSuccessRate,
                    baseDustByRarity,
                    dustPerEnhanceLevel,
                    stoneRefundShare),
            ]);

    /// <summary>A content set with <b>no</b> <c>tuning/forge.json</c> at all — the missing-document door.</summary>
    internal static ContentSnapshot Without() =>
        new(ProgressionDocuments.Shipped.Version, [ProgressionDocuments.Shipped.GetDocument(ProgressionDocuments.DocumentPath)]);

    /// <summary>A band-keyed price map, carrying an authored <c>null</c> where the row has one.</summary>
    /// <param name="rows">The bands and their prices; a null price is an authored absence.</param>
    /// <returns>The map.</returns>
    internal static ContentValue Prices(params (string Band, long? Price)[] rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var members = new Dictionary<string, ContentValue>(StringComparer.Ordinal);

        foreach (var (band, price) in rows)
        {
            members[band] = price is { } value
                ? ContentValue.Number(value)
                : ContentValue.Unauthorised;
        }

        return ContentValue.Object(members);
    }

    /// <summary>A ladder of whole numbers, in the order given.</summary>
    /// <param name="values">The rungs.</param>
    /// <returns>The array.</returns>
    internal static ContentValue Ladder(params long[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return ContentValue.Array(values.Select(value => ContentValue.Number(value)));
    }

    /// <summary>A ladder of fractional numbers, in the order given.</summary>
    /// <remarks>
    /// Named apart from <see cref="Ladder(long[])"/> rather than overloaded: an empty array literal
    /// binds to neither, and the two ladders in this document are a whole-number one and a
    /// fractional one that sit side by side in every case that replaces either.
    /// </remarks>
    /// <param name="values">The rungs.</param>
    /// <returns>The array.</returns>
    internal static ContentValue Rates(params double[] values)
    {
        ArgumentNullException.ThrowIfNull(values);

        return ContentValue.Array(values.Select(value => ContentValue.Number((decimal)value)));
    }

    /// <summary>The document itself, for a content set that carries other documents beside it.</summary>
    /// <returns>The shipped forge document.</returns>
    internal static ContentDocument ShippedDocument() => Shipped.GetDocument(DocumentPath);

    private static ContentDocument Document(
        ContentValue? inputCount,
        ContentValue? dustSubstituteMaxInputs,
        ContentValue? dustSubstituteCost,
        ContentValue? crownCost,
        ContentValue? minLevel,
        ContentValue? maxLevel,
        ContentValue? statBonusPerLevel,
        ContentValue? stoneCostPerLevel,
        ContentValue? perLevelSuccessRate,
        ContentValue? baseDustByRarity,
        ContentValue? dustPerEnhanceLevel,
        ContentValue? stoneRefundShare) =>
        new(
            DocumentPath,
            ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
            {
                ["merge"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                {
                    ["inputCount"] = inputCount ?? ContentValue.Number(ShippedMergeInputCount),
                    ["dustSubstituteMaxInputs"] =
                        dustSubstituteMaxInputs ?? ContentValue.Number(ShippedDustSubstituteMaxInputs),
                    ["dustSubstituteCost"] =
                        dustSubstituteCost ?? Prices([.. ShippedDustSubstituteCosts]),
                    ["crownCostByOutputRarity"] = crownCost ?? Prices([.. ShippedCrownCosts]),
                }),
                ["enhance"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                {
                    ["minLevel"] = minLevel ?? ContentValue.Number(ShippedMinEnhanceLevel),
                    ["maxLevel"] = maxLevel ?? ContentValue.Number(ShippedMaxEnhanceLevel),
                    ["statBonusPerLevel"] =
                        statBonusPerLevel ?? ContentValue.Number((decimal)ShippedStatBonusPerLevel),
                    ["stoneCostPerLevel"] = stoneCostPerLevel ?? Ladder([.. ShippedStoneCosts]),
                    ["perLevelSuccessRate"] = perLevelSuccessRate ?? Rates([.. ShippedSuccessRates]),
                }),
                ["salvage"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                {
                    ["baseDustByRarity"] = baseDustByRarity ?? Prices([.. ShippedBaseDust]),
                    ["dustPerEnhanceLevel"] =
                        dustPerEnhanceLevel ?? ContentValue.Number((decimal)ShippedDustPerEnhanceLevel),
                    ["stoneRefundShare"] =
                        stoneRefundShare ?? ContentValue.Number((decimal)ShippedStoneRefundShare),
                }),
                ["inventory"] = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                {
                    ["baseCapacity"] = ContentValue.Number(InventoryDocuments.ShippedBaseCapacity),
                    ["expansionStep"] = ContentValue.Number(InventoryDocuments.ShippedExpansionStep),
                    ["maxCapacity"] = ContentValue.Number(InventoryDocuments.ShippedMaxCapacity),
                }),
            }));
}
