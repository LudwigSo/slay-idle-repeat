using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic <c>tuning/currencies.json#/shopTile</c> fixtures, built as a <see cref="ContentSnapshot"/>
/// in memory.
/// </summary>
/// <remarks>
/// <c>Core.Tests</c> is hermetic, so this mirrors the shipped file rather than reading it — the same
/// shape <see cref="ProgressionDocuments"/> already uses for <c>tuning/progression.json</c>. The
/// shipped constants below are transcribed from <c>game-data/tuning/currencies.json#/shopTile</c>.
/// </remarks>
internal static class CurrenciesDocuments
{
    /// <summary>The document path the shop tunables live at.</summary>
    internal const string DocumentPath = "tuning/currencies.json";

    internal const int ShippedSlots = 4;
    internal const int ShippedFreeRefreshesPerVisit = 1;
    internal const decimal ShippedStagePriceStep = 0.25m;
    internal const decimal ShippedHealPctMaxHp = 0.35m;
    internal const long ShippedHealBasePrice = 150;
    internal const decimal ShippedLeftoverGoldAlarmShare = 0.2m;

    internal static readonly decimal[] ShippedChapterPriceScalar =
        { 1.0m, 1.55m, 2.4m, 3.72m, 5.77m, 8.95m, 13.86m, 21.5m };

    internal static readonly (string Id, long Price)[] ShippedPerkBasePrice =
        { ("COMMON", 180), ("RARE", 320), ("EPIC", 560), ("LEGENDARY", 950) };

    internal static readonly (string Id, long Price)[] ShippedConsumableBasePrice =
    {
        ("CON_HEALTH_DRAUGHT", 140),
        ("CON_DRAFT_TOKEN", 160),
        ("CON_ESCAPE_ROPE", 100),
    };

    internal static readonly (string Id, long Price)[] ShippedRunBuffBasePrice =
        { ("WHETSTONE", 300), ("HEARTROOT_TONIC", 300), ("HAWKS_EYE", 280) };

    internal static readonly (string Id, string Stat, decimal Base, decimal ChapterGrowth)[] ShippedRunBuffs =
    {
        ("WHETSTONE", "ATK", 12m, 2.0m),
        ("HEARTROOT_TONIC", "MAX_HP", 90m, 2.0m),
        ("HAWKS_EYE", "CRIT", 0.04m, 1.0m),
    };

    /// <summary>A snapshot holding exactly the shipped shop block.</summary>
    internal static ContentSnapshot Shipped { get; } = With();

    /// <summary>
    /// A snapshot holding the shipped shop block with individual leaves replaced. Pass
    /// <see cref="ContentValue.Unauthorised"/> to model a deliberate <c>null</c> hole, or omit a
    /// parameter to keep the shipped value.
    /// </summary>
    internal static ContentSnapshot With(
        ContentValue? slots = null,
        ContentValue? freeRefreshesPerVisit = null,
        ContentValue? stagePriceStep = null,
        ContentValue? chapterPriceScalar = null,
        ContentValue? perkBasePrice = null,
        ContentValue? consumableBasePrice = null,
        ContentValue? runBuffBasePrice = null,
        ContentValue? healBasePrice = null,
        ContentValue? healPctMaxHp = null,
        ContentValue? runBuffs = null,
        ContentValue? leftoverGoldAlarmShare = null)
    {
        var basePrice = ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["PERK"] = perkBasePrice ?? Table(ShippedPerkBasePrice),
            ["CONSUMABLE"] = consumableBasePrice ?? Table(ShippedConsumableBasePrice),
            ["RUN_BUFF"] = runBuffBasePrice ?? Table(ShippedRunBuffBasePrice),
            ["HEAL"] = healBasePrice ?? ContentValue.Number(ShippedHealBasePrice),
        });

        var shopTile = ShopTile(
            slots, freeRefreshesPerVisit, stagePriceStep, chapterPriceScalar, basePrice,
            healPctMaxHp, runBuffs, leftoverGoldAlarmShare);

        return Document(ContentValue.Object(
            new Dictionary<string, ContentValue>(StringComparer.Ordinal) { ["shopTile"] = shopTile }));
    }

    /// <summary>
    /// The <c>shopTile</c> block alone, so a fixture that builds the WHOLE of
    /// <c>tuning/currencies.json</c> can include it beside its own blocks.
    /// </summary>
    /// <remarks>
    /// Exposed rather than transcribed a second time in <c>InRunIncomeDocuments</c>: that fixture
    /// replaces the whole document, so a shop tile missing from it made every handler reading a
    /// price throw — and two fixtures spelling the same block would be free to disagree about a
    /// price the pricing tests then prove correct against the wrong one.
    /// </remarks>
    internal static ContentValue ShopTile(
        ContentValue? slots = null,
        ContentValue? freeRefreshesPerVisit = null,
        ContentValue? stagePriceStep = null,
        ContentValue? chapterPriceScalar = null,
        ContentValue? basePrice = null,
        ContentValue? healPctMaxHp = null,
        ContentValue? runBuffs = null,
        ContentValue? leftoverGoldAlarmShare = null)
    {
        basePrice ??= ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["PERK"] = Table(ShippedPerkBasePrice),
            ["CONSUMABLE"] = Table(ShippedConsumableBasePrice),
            ["RUN_BUFF"] = Table(ShippedRunBuffBasePrice),
            ["HEAL"] = ContentValue.Number(ShippedHealBasePrice),
        });

        return ContentValue.Object(new Dictionary<string, ContentValue>(StringComparer.Ordinal)
        {
            ["slots"] = slots ?? ContentValue.Number(ShippedSlots),
            ["freeRefreshesPerVisit"] = freeRefreshesPerVisit ?? ContentValue.Number(ShippedFreeRefreshesPerVisit),
            ["stagePriceStep"] = stagePriceStep ?? ContentValue.Number(ShippedStagePriceStep),
            ["chapterPriceScalar"] = chapterPriceScalar ?? ContentValue.Array(
                ShippedChapterPriceScalar.Select(v => ContentValue.Number(v))),
            ["basePrice"] = basePrice,
            ["healPctMaxHp"] = healPctMaxHp ?? ContentValue.Number(ShippedHealPctMaxHp),
            ["runBuffs"] = runBuffs ?? ContentValue.Array(ShippedRunBuffs.Select(b => ContentValue.Object(
                new Dictionary<string, ContentValue>(StringComparer.Ordinal)
                {
                    ["id"] = ContentValue.Text(b.Id),
                    ["displayName"] = ContentValue.Text("loc.runbuff." + b.Id.ToLowerInvariant() + ".name"),
                    ["stat"] = ContentValue.Text(b.Stat),
                    ["base"] = ContentValue.Number(b.Base),
                    ["chapterGrowth"] = ContentValue.Number(b.ChapterGrowth),
                }))),
            ["leftoverGoldAlarmShare"] = leftoverGoldAlarmShare ?? ContentValue.Number(ShippedLeftoverGoldAlarmShare),
        });
    }

    /// <summary>A snapshot whose <c>tuning/currencies.json</c> has the given root value.</summary>
    internal static ContentSnapshot Document(ContentValue root) =>
        new(
            ContentVersion.FromHex(new string('c', ContentVersion.HexLength)),
            [new ContentDocument(DocumentPath, root)]);

    private static ContentValue Table(IEnumerable<(string Id, long Price)> rows) =>
        ContentValue.Object(rows.Select(r =>
            new KeyValuePair<string, ContentValue>(r.Id, ContentValue.Number(r.Price))));
}
