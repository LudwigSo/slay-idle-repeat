using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content.Perks;

/// <summary>
/// Hermetic <c>content/perks/perks.json</c> fixtures — a small catalogue spanning every rarity band
/// and more than one category, enough to exercise <c>PerkCatalogue</c> and the draft engine without
/// depending on the shipped catalogue.
/// </summary>
internal static class PerkDocuments
{
    internal const string DocumentPath = "content/perks/perks.json";

    /// <summary>One Common, offense. Never owned in any fixture unless a test says so.</summary>
    internal const string Common1 = "PK_TEST_COMMON_1";

    /// <summary>A second Common, defense — so the Common band has more than one row.</summary>
    internal const string Common2 = "PK_TEST_COMMON_2";

    internal const string Rare1 = "PK_TEST_RARE_1";
    internal const string Epic1 = "PK_TEST_EPIC_1";
    internal const string Legendary1 = "PK_TEST_LEGENDARY_1";

    /// <summary>Every id this fixture authors, in the document's order.</summary>
    internal static readonly string[] AllIds =
    {
        Common1, Common2, Rare1, Epic1, Legendary1,
    };

    /// <summary>A second Offense Common, so one category can hold more rows than a draft has slots.</summary>
    /// <remarks>
    /// Only in <see cref="OneCategoryDominates"/>. The five-row catalogue above gives every perk its
    /// own category, which makes "no duplicate options" and "at least two categories" indistinguishable
    /// — a draft of three distinct perks is diverse there by construction, so a diversity rule that
    /// did nothing would pass. This row and the next are what tell the two rules apart.
    /// </remarks>
    internal const string OffenseCommon2 = "PK_TEST_OFFENSE_COMMON_2";

    /// <summary>A third Offense Common. Three of them is what lets an unconstrained draft go mono-category.</summary>
    internal const string OffenseCommon3 = "PK_TEST_OFFENSE_COMMON_3";

    /// <summary>The document, for composing into a larger fixture snapshot alongside it.</summary>
    internal static ContentDocument Document { get; } = new(DocumentPath, Root());

    /// <summary>
    /// A catalogue whose Common band is three Offense rows and one Defense row, plus one row in each
    /// higher band — the shape the category-diversity rule has to bite on.
    /// </summary>
    internal static ContentSnapshot OneCategoryDominates { get; } =
        new(
            ContentVersion.FromHex(new string('d', ContentVersion.HexLength)),
            [
                new ContentDocument(DocumentPath, Obj(
                    ("perks", ContentValue.Array(new[]
                    {
                        Perk(Common1, "OFFENSE", "COMMON"),
                        Perk(OffenseCommon2, "OFFENSE", "COMMON"),
                        Perk(OffenseCommon3, "OFFENSE", "COMMON"),
                        Perk(Common2, "DEFENSE", "COMMON"),
                        Perk(Rare1, "SUSTAIN", "RARE"),
                        Perk(Epic1, "POISON", "EPIC"),
                        Perk(Legendary1, "FIRE", "LEGENDARY"),
                    })))),
            ]);

    /// <summary>The base perk of the gated fixture's one gated category.</summary>
    internal const string GateBase = "PK_TEST_GATE_BASE";

    /// <summary>A perk behind <see cref="GateBase"/>. Undraftable until the base is owned.</summary>
    internal const string BehindTheGate = "PK_TEST_BEHIND_GATE";

    /// <summary>A second perk behind the same base, so an opened gate has more than one row to offer.</summary>
    internal const string AlsoBehindTheGate = "PK_TEST_ALSO_BEHIND_GATE";

    /// <summary>
    /// A catalogue with one gated category: an un-gated base and two perks that require it, beside
    /// the un-gated rows of the five-row set.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="Shipped"/> rather than folded into it, because that fixture's rows
    /// are the subject of the band and diversity cases and adding gated rows to it would change what
    /// every one of them draws from. Here the gate is the only thing that varies.
    /// </remarks>
    internal static ContentSnapshot WithAGatedCategory { get; } =
        new(
            ContentVersion.FromHex(new string('e', ContentVersion.HexLength)),
            [
                new ContentDocument(DocumentPath, Obj(
                    ("perks", ContentValue.Array(new[]
                    {
                        Perk(Common1, "OFFENSE", "COMMON"),
                        Perk(Common2, "DEFENSE", "COMMON"),
                        Perk(GateBase, "FIRE", "COMMON"),
                        Perk(BehindTheGate, "FIRE", "RARE", requires: GateBase),
                        Perk(AlsoBehindTheGate, "FIRE", "EPIC", requires: GateBase),
                        Perk(Legendary1, "SUSTAIN", "LEGENDARY"),
                    })))),
            ]);

    /// <summary>The four categories the deep-band catalogue spreads each rarity across.</summary>
    private static readonly string[] DeepBandCategories = ["OFFENSE", "DEFENSE", "SUSTAIN", "FIRE"];

    /// <summary>The four rarity bands the deep-band catalogue authors in every category.</summary>
    private static readonly string[] DeepBandRarities = ["COMMON", "RARE", "EPIC", "LEGENDARY"];

    /// <summary>
    /// Four categories × all four bands, as a document to compose beside the draft's other tuning.
    /// </summary>
    /// <remarks>
    /// 🔒 The shape a "this draft offers only Epic+" case needs. The draft engine falls through to
    /// the unbanded pool whenever the band it drew is empty, so on the five-row catalogue above an
    /// epic+ table and a Common-heavy one are indistinguishable: both end up offering the two
    /// highest rows. Every band being populated in enough categories to satisfy the diversity
    /// narrowing is what makes the offered bands evidence of the weights.
    /// </remarks>
    internal static ContentDocument DeepBandsDocument { get; } = new(DocumentPath, Obj(
        ("perks", ContentValue.Array(
            DeepBandCategories.SelectMany(
                category => DeepBandRarities.Select(
                    rarity => Perk(DeepBandId(category, rarity), category, rarity)))))));

    /// <summary>The id the deep-band catalogue gives one (category, band) pair.</summary>
    internal static string DeepBandId(string category, string rarity) =>
        "PK_TEST_DEEP_" + category + "_" + rarity;

    /// <summary>The whole fixture catalogue, three tiers each.</summary>
    internal static ContentSnapshot Shipped { get; } = Build();

    /// <summary>A snapshot carrying only this document — for tests that need nothing else.</summary>
    private static ContentSnapshot Build() =>
        new(ContentVersion.FromHex(new string('c', ContentVersion.HexLength)), [Document]);

    private static ContentValue Root() => Obj(
        ("perks", ContentValue.Array(new[]
        {
            Perk(Common1, "OFFENSE", "COMMON"),
            Perk(Common2, "DEFENSE", "COMMON"),
            Perk(Rare1, "SUSTAIN", "RARE"),
            Perk(Epic1, "POISON", "EPIC"),
            Perk(Legendary1, "FIRE", "LEGENDARY"),
        })));

    private static ContentValue Perk(
        string id, string category, string rarity, string? requires = null) => Obj(
        ("id", ContentValue.Text(id)),
        ("name", ContentValue.Text(id)),
        ("category", ContentValue.Text(category)),
        ("rarity", ContentValue.Text(rarity)),
        ("iconId", ContentValue.Text("icon_perk_test")),
        ("description", ContentValue.Text("+{value}% Test.")),
        ("tiers", ContentValue.Array(new[]
        {
            Tier(1, id, 0.10m),
            Tier(2, id, 0.18m),
            Tier(3, id, 0.28m),
        })),
        ("excludes", ContentValue.Array(Array.Empty<ContentValue>())),
        ("requires", ContentValue.Array(
            requires is null ? Array.Empty<ContentValue>() : new[] { ContentValue.Text(requires) })),
        ("poolTags", ContentValue.Array(new[] { ContentValue.Text("standard") })));

    private static ContentValue Tier(int tier, string id, decimal value) => Obj(
        ("tier", ContentValue.Number(tier)),
        ("effects", ContentValue.Array(new[]
        {
            Obj(
                ("id", ContentValue.Text(id + "_T" + tier)),
                ("op", ContentValue.Text("STAT_ADD_PCT")),
                ("stat", ContentValue.Text("ATK")),
                ("trigger", Obj(("kind", ContentValue.Text("ALWAYS")))),
                ("target", ContentValue.Text("SELF")),
                ("value", ContentValue.Number(value))),
        })));

    private static ContentValue Obj(params (string Name, ContentValue Value)[] members) =>
        ContentValue.Object(members.Select(m => new KeyValuePair<string, ContentValue>(m.Name, m.Value)));
}
