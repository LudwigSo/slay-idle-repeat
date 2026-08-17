using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>
/// Hermetic fixtures for the three documents gear generation reads: the twenty-four base items in
/// <c>content/gear/gear.json</c>, the generation tables in <c>tuning/drops.json</c>, and the content
/// par curve in <c>tuning/par_power.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// <c>Core.Tests</c> is hermetic, so this mirrors the shipped files rather than reading them, on
/// <see cref="LuckDocuments"/>' precedent. The other half of the claim — that these <em>are</em> the
/// numbers we ship — belongs to <c>Application.Tests</c>, which resolves the same pointers in the real
/// files. Neither half is sufficient alone: this proves the rules are right about the numbers, that
/// one proves those are the numbers.
/// </para>
/// <para>
/// 🔴 <b>That counterpart is <c>GearDocumentsMatchesTuningDataTests</c>, and until M4-15 it did not
/// exist</b> — the paragraph above described it in the present tense for three tasks while every
/// number below rested on a transcription nothing tied to <c>game-data/</c>. It is named here rather
/// than merely alluded to, because "a counterpart belongs to Application.Tests" is a sentence that
/// reads exactly the same whether or not anybody wrote one. When a value is added below, add it
/// there in the same commit.
/// </para>
/// <para>
/// Only what a reader actually reads is transcribed. <c>par_power.json</c>'s Heroic and Mythic
/// columns, <c>drops.json</c>'s container tables and its acquisition rates are all absent for that
/// reason: nothing under <c>Rules/Gear/</c> reads them, and authoring them here would imply something
/// does.
/// </para>
/// <para>
/// The three documents are always present together, which is deliberate. A reader that resolved the
/// wrong document path would be indistinguishable from one that resolved the right one in a snapshot
/// of size one; <see cref="Without"/> is how a case asks for a document to be missing.
/// </para>
/// </remarks>
internal static class GearDocuments
{
    /// <summary>Where the twenty-four base items live.</summary>
    internal const string GearDocumentPath = "content/gear/gear.json";

    /// <summary>Where the gear generation tables live.</summary>
    internal const string DropsDocumentPath = "tuning/drops.json";

    /// <summary>Where the content par curve lives.</summary>
    internal const string ParPowerDocumentPath = "tuning/par_power.json";

    // ---------------------------------------------------------------- the rarity ladder

    /// <summary>The bottom band's multiplier on item power.</summary>
    internal const decimal ShippedStatMultiplierC = 1.0m;

    /// <summary>The second band's multiplier on item power.</summary>
    internal const decimal ShippedStatMultiplierB = 1.55m;

    /// <summary>The third band's multiplier on item power.</summary>
    internal const decimal ShippedStatMultiplierA = 2.4m;

    /// <summary>The fourth band's multiplier on item power.</summary>
    internal const decimal ShippedStatMultiplierS = 3.8m;

    /// <summary>The top band's multiplier on item power.</summary>
    internal const decimal ShippedStatMultiplierSs = 6.0m;

    /// <summary>How many affixes the bottom band rolls.</summary>
    internal const int ShippedAffixCountC = 0;

    /// <summary>How many affixes the second band rolls.</summary>
    internal const int ShippedAffixCountB = 1;

    /// <summary>How many affixes the third band rolls.</summary>
    internal const int ShippedAffixCountA = 2;

    /// <summary>How many affixes the fourth band rolls.</summary>
    internal const int ShippedAffixCountS = 3;

    /// <summary>How many affixes the top band rolls.</summary>
    internal const int ShippedAffixCountSs = 4;

    // ---------------------------------------------------------------- item generation

    /// <summary>The fraction of a chapter's power target one item carries.</summary>
    internal const decimal ShippedItemPowerCoefficient = 0.1m;

    /// <summary>The bottom of the quality range.</summary>
    internal const decimal ShippedQualityMinimum = 0.0m;

    /// <summary>The top of the quality range.</summary>
    internal const decimal ShippedQualityMaximum = 1.0m;

    /// <summary>The primary stat's multiplier at the bottom of the quality range.</summary>
    internal const decimal ShippedPrimaryScaleBase = 0.9m;

    /// <summary>How far the primary stat's multiplier climbs across the quality range.</summary>
    internal const decimal ShippedPrimaryScaleSpan = 0.2m;

    /// <summary>The secondary stat's multiplier at the bottom of the quality range.</summary>
    internal const decimal ShippedSecondaryScaleBase = 0.85m;

    /// <summary>How far the secondary stat's multiplier climbs across the quality range.</summary>
    internal const decimal ShippedSecondaryScaleSpan = 0.3m;

    // ---------------------------------------------------------------- sets and the affix pool

    /// <summary>The piece counts a set bonus fires at, ascending.</summary>
    internal static IReadOnlyList<int> ShippedSetBreakpoints { get; } = [2, 4, 6];

    /// <summary>The affix that carries a rarity floor, and the band it floors at.</summary>
    internal const string ShippedFlooredAffixId = "AFX_REROLL_CHARGE";

    /// <summary>The band <see cref="ShippedFlooredAffixId"/> first becomes eligible at.</summary>
    internal const string ShippedFlooredAffixMinimumRarity = "S";

    /// <summary>How many affixes the pool authors.</summary>
    internal const int ShippedAffixPoolSize = 14;

    // ---------------------------------------------------------------- the base-item roster

    /// <summary>
    /// The twenty-four base items as the document authors them: id, slot, family, family axis.
    /// </summary>
    /// <remarks>
    /// Exposed rather than private because it is a transcription the catalogue's own cases read back
    /// — a fixture that only the fixture could see would let the roster and the assertions drift
    /// apart without either noticing.
    /// </remarks>
    internal static IReadOnlyList<AuthoredBaseItem> ShippedBaseItems { get; } =
    [
        new("GEAR_WEAPON_BLADE", "WEAPON", "BLADE", "BALANCED"),
        new("GEAR_WEAPON_AXE", "WEAPON", "AXE", "HEAVY"),
        new("GEAR_WEAPON_STAFF", "WEAPON", "STAFF", "CASTER"),
        new("GEAR_WEAPON_BOW", "WEAPON", "BOW", "AGILE"),
        new("GEAR_HELMET_HOOD", "HELMET", "HOOD", "BALANCED"),
        new("GEAR_HELMET_HELM", "HELMET", "HELM", "HEAVY"),
        new("GEAR_HELMET_CIRCLET", "HELMET", "CIRCLET", "CASTER"),
        new("GEAR_HELMET_MASK", "HELMET", "MASK", "AGILE"),
        new("GEAR_ARMOR_LEATHERS", "ARMOR", "LEATHERS", "BALANCED"),
        new("GEAR_ARMOR_PLATE", "ARMOR", "PLATE", "HEAVY"),
        new("GEAR_ARMOR_ROBE", "ARMOR", "ROBE", "CASTER"),
        new("GEAR_ARMOR_SCALEMAIL", "ARMOR", "SCALEMAIL", "AGILE"),
        new("GEAR_BOOTS_TREADS", "BOOTS", "TREADS", "BALANCED"),
        new("GEAR_BOOTS_GREAVES", "BOOTS", "GREAVES", "HEAVY"),
        new("GEAR_BOOTS_SLIPPERS", "BOOTS", "SLIPPERS", "CASTER"),
        new("GEAR_BOOTS_SANDALS", "BOOTS", "SANDALS", "AGILE"),
        new("GEAR_RING_BAND", "RING", "BAND", "BALANCED"),
        new("GEAR_RING_SIGNET", "RING", "SIGNET", "HEAVY"),
        new("GEAR_RING_LOOP", "RING", "LOOP", "CASTER"),
        new("GEAR_RING_SEAL", "RING", "SEAL", "AGILE"),
        new("GEAR_AMULET_PENDANT", "AMULET", "PENDANT", "BALANCED"),
        new("GEAR_AMULET_TALISMAN", "AMULET", "TALISMAN", "HEAVY"),
        new("GEAR_AMULET_CHARM", "AMULET", "CHARM", "CASTER"),
        new("GEAR_AMULET_IDOL", "AMULET", "IDOL", "AGILE"),
    ];

    /// <summary>The four chapter bands and the shares they author, as percentages.</summary>
    internal static IReadOnlyList<AuthoredChapterBand> ShippedChapterBands { get; } =
    [
        new(1, 2, [("C", 60m), ("B", 27m), ("A", 10m), ("S", 2.7m), ("SS", 0.3m)]),
        new(3, 4, [("C", 40m), ("B", 36m), ("A", 18m), ("S", 5.4m), ("SS", 0.6m)]),
        new(5, 6, [("C", 15m), ("B", 40m), ("A", 32m), ("S", 11m), ("SS", 2m)]),
        new(7, 8, [("C", 0m), ("B", 28m), ("A", 44m), ("S", 23m), ("SS", 5m)]),
    ];

    /// <summary>The Normal column of the content par table, chapter by chapter.</summary>
    internal static IReadOnlyList<(int Chapter, decimal Normal)> ShippedParPower { get; } =
    [
        (1, 1000m), (2, 2000m), (3, 4000m), (4, 8000m),
        (5, 16000m), (6, 32000m), (7, 64000m), (8, 128000m),
    ];

    /// <summary>The six slot rows: the two stats each slot carries, and their coefficients.</summary>
    /// <remarks>
    /// A <see langword="null"/> coefficient is the authored statement that the stat is a percentage
    /// read from the per-rarity table — not a missing value.
    /// </remarks>
    internal static IReadOnlyList<AuthoredSlotRow> ShippedSlotRows { get; } =
    [
        new("WEAPON", "ATK", 0.2m, "CRIT", null),
        new("HELMET", "DEF", 0.18m, "MAX_HP", 0.55m),
        new("ARMOR", "MAX_HP", 1.1m, "DEF", 0.12m),
        new("BOOTS", "ASPD", null, "DODGE", null),
        new("RING", "CRIT", null, "PEN", null),
        new("AMULET", "MAX_HP", 0.45m, "LIFESTEAL", null),
    ];

    /// <summary>The five percent stats, each with one value per band.</summary>
    internal static IReadOnlyList<AuthoredPercentStat> ShippedPercentStats { get; } =
    [
        new("CRIT", 0.015m, 0.025m, 0.04m, 0.06m, 0.08m),
        new("ASPD", 0.02m, 0.03m, 0.045m, 0.07m, 0.1m),
        new("DODGE", 0.01m, 0.015m, 0.025m, 0.04m, 0.055m),
        new("PEN", 0.02m, 0.035m, 0.05m, 0.08m, 0.11m),
        new("LIFESTEAL", 0.015m, 0.025m, 0.04m, 0.06m, 0.08m),
    ];

    /// <summary>The fourteen affixes: id, authored range, the slots they may roll on, and any floor.</summary>
    internal static IReadOnlyList<AuthoredAffix> ShippedAffixes { get; } =
    [
        new("AFX_CRIT_CHANCE", 0.02m, 0.08m, ["WEAPON", "RING", "HELMET"], null),
        new("AFX_CRIT_DAMAGE", 0.1m, 0.35m, ["WEAPON", "RING"], null),
        new("AFX_ATTACK_SPEED", 0.03m, 0.12m, ["WEAPON", "BOOTS"], null),
        new("AFX_PEN", 0.04m, 0.15m, ["WEAPON", "RING"], null),
        new("AFX_MAX_HP", 0.05m, 0.2m, ["ARMOR", "HELMET", "AMULET"], null),
        new("AFX_DEF", 0.05m, 0.22m, ["ARMOR", "HELMET", "BOOTS"], null),
        new("AFX_DODGE", 0.02m, 0.08m, ["BOOTS", "AMULET"], null),
        new("AFX_BLOCK", 0.03m, 0.12m, ["ARMOR", "HELMET"], null),
        new("AFX_LIFESTEAL", 0.02m, 0.09m, ["AMULET", "WEAPON"], null),
        new("AFX_DAMAGE_REDUCTION", 0.02m, 0.08m, ["ARMOR", "AMULET"], null),
        new("AFX_GOLD_GAIN", 0.08m, 0.3m, ["RING", "AMULET"], null),
        new("AFX_PET_AURA_POWER", 0.05m, 0.2m, ["AMULET", "RING"], null),
        new(ShippedFlooredAffixId, 1m, 1m, ["RING", "AMULET"], ShippedFlooredAffixMinimumRarity),
        new("AFX_DAMAGE_VS_ELITES", 0.08m, 0.25m, ["WEAPON", "RING"], null),
    ];

    /// <summary>All three shipped documents, together.</summary>
    internal static ContentSnapshot Shipped { get; } = With();

    /// <summary>
    /// The three documents with individual blocks replaced. Pass
    /// <see cref="ContentValue.Unauthorised"/> for a deliberate <c>null</c> hole, or omit to keep the
    /// shipped value.
    /// </summary>
    /// <remarks>
    /// Whole blocks rather than a parameter per leaf. Each reader here refuses a <em>shape</em> — a
    /// grid missing a family, a chapter band that does not add up, a non-ascending breakpoint ladder,
    /// a chapter authored twice — and none of those can be driven one leaf at a time.
    /// </remarks>
    /// <param name="slots">The whole <c>slots</c> array of the base-item catalogue.</param>
    /// <param name="rarities">The whole rarity ladder.</param>
    /// <param name="dropShares">The whole chapter-banded drop table.</param>
    /// <param name="itemPowerCoefficient">The fraction of a chapter's par one item carries.</param>
    /// <param name="qualityMinimum">The bottom of the quality range.</param>
    /// <param name="qualityMaximum">The top of the quality range.</param>
    /// <param name="slotCoefficients">The whole slot coefficient table.</param>
    /// <param name="percentStats">The whole percent-stat table.</param>
    /// <param name="affixes">The whole affix pool.</param>
    /// <param name="setBreakpoints">The whole breakpoint ladder.</param>
    /// <param name="parPower">The whole par table.</param>
    internal static ContentSnapshot With(
        ContentValue? slots = null,
        ContentValue? rarities = null,
        ContentValue? dropShares = null,
        ContentValue? itemPowerCoefficient = null,
        ContentValue? qualityMinimum = null,
        ContentValue? qualityMaximum = null,
        ContentValue? slotCoefficients = null,
        ContentValue? percentStats = null,
        ContentValue? affixes = null,
        ContentValue? setBreakpoints = null,
        ContentValue? parPower = null) =>
        new(
            ProgressionDocuments.Shipped.Version,
            [
                new ContentDocument(GearDocumentPath, Members(("slots", slots ?? Slots()))),
                new ContentDocument(
                    DropsDocumentPath,
                    Drops(
                        rarities,
                        dropShares,
                        itemPowerCoefficient,
                        qualityMinimum,
                        qualityMaximum,
                        slotCoefficients,
                        percentStats,
                        affixes,
                        setBreakpoints)),
                new ContentDocument(
                    ParPowerDocumentPath,
                    Members(("parPower", parPower ?? ParPower()))),
            ]);

    /// <summary>The shipped set with one document removed entirely.</summary>
    /// <remarks>
    /// The "missing document" case, which is a different failure from "the pointer holds null", from
    /// "the leaf is the wrong kind" and from "the value is authorised but unusable".
    /// </remarks>
    /// <param name="documentPath">The document to leave out.</param>
    internal static ContentSnapshot Without(string documentPath) =>
        new(
            Shipped.Version,
            Shipped.DocumentPaths
                .Where(path => !string.Equals(path, documentPath, StringComparison.Ordinal))
                .Select(Shipped.GetDocument));

    // ---------------------------------------------------------------- builders

    /// <summary>One slot block of the base-item catalogue.</summary>
    /// <param name="slot">The authored slot token.</param>
    /// <param name="families">The family rows it carries.</param>
    internal static ContentValue SlotBlock(string slot, params ContentValue[] families) =>
        Members(("slot", ContentValue.Text(slot)), ("families", ContentValue.Array(families)));

    /// <summary>One family row of a slot block.</summary>
    /// <param name="id">The authored base-item id.</param>
    /// <param name="family">The authored family token.</param>
    /// <param name="axis">The authored family-axis token.</param>
    internal static ContentValue FamilyRow(string id, string family, string axis) =>
        Members(
            ("id", ContentValue.Text(id)),
            ("family", ContentValue.Text(family)),
            ("familyAxis", ContentValue.Text(axis)));

    /// <summary>The shipped <c>slots</c> array, built from <see cref="ShippedBaseItems"/>.</summary>
    internal static ContentValue Slots() => ContentValue.Array(
        ShippedBaseItems
            .Select(item => item.Slot)
            .Distinct(StringComparer.Ordinal)
            .Select(slot => SlotBlock(
                slot,
                ShippedBaseItems
                    .Where(item => string.Equals(item.Slot, slot, StringComparison.Ordinal))
                    .Select(item => FamilyRow(item.DefId, item.Family, item.Axis))
                    .ToArray())));

    /// <summary>One chapter band of the drop table.</summary>
    /// <param name="from">The first chapter it covers.</param>
    /// <param name="to">The last chapter it covers.</param>
    /// <param name="shares">Each band's percentage.</param>
    internal static ContentValue ShareBand(
        ContentValue from, ContentValue to, params (string Rarity, decimal Share)[] shares) =>
        Members(
            ("chapterFrom", from),
            ("chapterTo", to),
            ("share", Members(shares
                .Select(share => (share.Rarity, ContentValue.Number(share.Share)))
                .ToArray())));

    /// <summary>The shipped chapter-banded drop table.</summary>
    internal static ContentValue DropShares() => ContentValue.Array(
        ShippedChapterBands.Select(band => ShareBand(
            ContentValue.Number(band.From),
            ContentValue.Number(band.To),
            band.Shares.ToArray())));

    /// <summary>One row of the par table. Only the Normal column is read.</summary>
    /// <param name="chapter">The chapter.</param>
    /// <param name="normal">Its Normal-tier par power.</param>
    internal static ContentValue ParRow(ContentValue chapter, ContentValue normal) =>
        Members(("chapter", chapter), ("NORMAL", normal));

    /// <summary>The shipped par table.</summary>
    internal static ContentValue ParPower() => ContentValue.Array(
        ShippedParPower.Select(row =>
            ParRow(ContentValue.Number(row.Chapter), ContentValue.Number(row.Normal))));

    /// <summary>One affix row of the pool. The rarity floor is omitted where the affix has none.</summary>
    /// <remarks>
    /// Omitted rather than authored as a null: the schema makes the floor optional, and the reader
    /// tells "no floor" from "a hole somebody left" by whether the member is there at all.
    /// </remarks>
    /// <param name="affix">The affix to author.</param>
    internal static ContentValue AffixRow(AuthoredAffix affix)
    {
        var members = new List<(string Name, ContentValue Value)>
        {
            ("id", ContentValue.Text(affix.AffixId)),
            ("min", ContentValue.Number(affix.Minimum)),
            ("max", ContentValue.Number(affix.Maximum)),
            ("slots", ContentValue.Array(affix.Slots.Select(ContentValue.Text))),
        };

        if (affix.MinimumRarity is { } floor)
        {
            members.Add(("minRarity", ContentValue.Text(floor)));
        }

        return Members(members.ToArray());
    }

    /// <summary>The shipped affix pool.</summary>
    internal static ContentValue Affixes() =>
        ContentValue.Array(ShippedAffixes.Select(AffixRow));

    private static ContentValue Drops(
        ContentValue? rarities,
        ContentValue? dropShares,
        ContentValue? itemPowerCoefficient,
        ContentValue? qualityMinimum,
        ContentValue? qualityMaximum,
        ContentValue? slotCoefficients,
        ContentValue? percentStats,
        ContentValue? affixes,
        ContentValue? setBreakpoints) =>
        Members(
            ("rarities", rarities ?? Rarities()),
            ("dropShareByChapterBand", dropShares ?? DropShares()),
            ("itemGeneration", Members(
                ("itemPowerCoefficient",
                    itemPowerCoefficient ?? ContentValue.Number(ShippedItemPowerCoefficient)),
                ("quality", Members(
                    ("min", qualityMinimum ?? ContentValue.Number(ShippedQualityMinimum)),
                    ("max", qualityMaximum ?? ContentValue.Number(ShippedQualityMaximum)),
                    ("primaryScale", Members(
                        ("base", ContentValue.Number(ShippedPrimaryScaleBase)),
                        ("span", ContentValue.Number(ShippedPrimaryScaleSpan)))),
                    ("secondaryScale", Members(
                        ("base", ContentValue.Number(ShippedSecondaryScaleBase)),
                        ("span", ContentValue.Number(ShippedSecondaryScaleSpan)))))))),
            ("slotCoefficients", slotCoefficients ?? SlotCoefficients()),
            ("percentStatsByRarity", percentStats ?? PercentStats()),
            ("affixPool", Members(("affixes", affixes ?? Affixes()))),
            ("sets", Members(("breakpoints", setBreakpoints ?? Breakpoints()))));

    private static ContentValue Rarities() => ContentValue.Array(
    [
        RarityRow("C", ShippedStatMultiplierC, ShippedAffixCountC),
        RarityRow("B", ShippedStatMultiplierB, ShippedAffixCountB),
        RarityRow("A", ShippedStatMultiplierA, ShippedAffixCountA),
        RarityRow("S", ShippedStatMultiplierS, ShippedAffixCountS),
        RarityRow("SS", ShippedStatMultiplierSs, ShippedAffixCountSs),
    ]);

    private static ContentValue RarityRow(string id, decimal statMultiplier, int affixCount) =>
        Members(
            ("id", ContentValue.Text(id)),
            ("statMultiplier", ContentValue.Number(statMultiplier)),
            ("affixCount", ContentValue.Number(affixCount)));

    private static ContentValue SlotCoefficients() => ContentValue.Array(
        ShippedSlotRows.Select(row => Members(
            ("slot", ContentValue.Text(row.Slot)),
            ("primaryStat", ContentValue.Text(row.PrimaryStat)),
            ("primaryCoef", Coefficient(row.PrimaryCoefficient)),
            ("secondaryStat", ContentValue.Text(row.SecondaryStat)),
            ("secondaryCoef", Coefficient(row.SecondaryCoefficient)))));

    private static ContentValue Coefficient(decimal? coefficient) =>
        coefficient is { } value ? ContentValue.Number(value) : ContentValue.Unauthorised;

    /// <summary>
    /// The percent-stat table, with the underscore-prefixed comment member the shipped file carries.
    /// </summary>
    /// <remarks>
    /// The comment member is authored on purpose: the reader skips names beginning with an
    /// underscore, and a fixture without one would leave that skip untested — a reader that stopped
    /// skipping would then refuse the shipped file while every case here stayed green.
    /// </remarks>
    private static ContentValue PercentStats()
    {
        var members = new List<(string Name, ContentValue Value)>
        {
            ("_doc", ContentValue.Text("percent stats scale with rarity only, never with chapter")),
        };

        foreach (var stat in ShippedPercentStats)
        {
            members.Add((stat.Stat, Members(
                ("C", ContentValue.Number(stat.C)),
                ("B", ContentValue.Number(stat.B)),
                ("A", ContentValue.Number(stat.A)),
                ("S", ContentValue.Number(stat.S)),
                ("SS", ContentValue.Number(stat.Ss)))));
        }

        return Members(members.ToArray());
    }

    private static ContentValue Breakpoints() => ContentValue.Array(
        ShippedSetBreakpoints.Select(pieces => ContentValue.Number(pieces)));

    private static ContentValue Members(params (string Name, ContentValue Value)[] members) =>
        ContentValue.Object(members.Select(member =>
            new KeyValuePair<string, ContentValue>(member.Name, member.Value)));
}

/// <summary>One of the twenty-four base items, as <c>content/gear/gear.json</c> authors it.</summary>
/// <param name="DefId">The authored id.</param>
/// <param name="Slot">The authored slot token.</param>
/// <param name="Family">The authored family token.</param>
/// <param name="Axis">The authored family-axis token.</param>
internal readonly record struct AuthoredBaseItem(string DefId, string Slot, string Family, string Axis);

/// <summary>One chapter band of the drop table, as <c>tuning/drops.json</c> authors it.</summary>
/// <param name="From">The first chapter it covers.</param>
/// <param name="To">The last chapter it covers.</param>
/// <param name="Shares">Each band's percentage.</param>
internal readonly record struct AuthoredChapterBand(
    int From, int To, IReadOnlyList<(string Rarity, decimal Share)> Shares);

/// <summary>One slot's two stats and their coefficients, as authored.</summary>
/// <param name="Slot">The authored slot token.</param>
/// <param name="PrimaryStat">The authored primary stat token.</param>
/// <param name="PrimaryCoefficient">Its coefficient, or null where the stat is a percentage.</param>
/// <param name="SecondaryStat">The authored secondary stat token.</param>
/// <param name="SecondaryCoefficient">Its coefficient, or null where the stat is a percentage.</param>
internal readonly record struct AuthoredSlotRow(
    string Slot,
    string PrimaryStat,
    decimal? PrimaryCoefficient,
    string SecondaryStat,
    decimal? SecondaryCoefficient);

/// <summary>One percent stat's value at each band, as authored.</summary>
/// <param name="Stat">The authored stat token.</param>
/// <param name="C">Its value at the bottom band.</param>
/// <param name="B">Its value at the second band.</param>
/// <param name="A">Its value at the third band.</param>
/// <param name="S">Its value at the fourth band.</param>
/// <param name="Ss">Its value at the top band.</param>
internal readonly record struct AuthoredPercentStat(
    string Stat, decimal C, decimal B, decimal A, decimal S, decimal Ss);

/// <summary>One affix of the pool, as authored.</summary>
/// <param name="AffixId">The authored id.</param>
/// <param name="Minimum">The bottom of its range, inclusive.</param>
/// <param name="Maximum">The top of its range, inclusive.</param>
/// <param name="Slots">The slots it may roll on.</param>
/// <param name="MinimumRarity">Its rarity floor, or null where it authors none.</param>
internal readonly record struct AuthoredAffix(
    string AffixId,
    decimal Minimum,
    decimal Maximum,
    IReadOnlyList<string> Slots,
    string? MinimumRarity);
