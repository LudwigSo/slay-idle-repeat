using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content.Perks;

/// <summary>
/// Hermetic <c>content/perks/perks.json</c> fixtures — a small catalogue spanning every rarity band
/// and more than one category, enough to exercise <c>PerkCatalogue</c> and the draft engine without
/// depending on the shipped 46-row set.
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
                        Perk(Epic1, "ECONOMY", "EPIC"),
                        Perk(Legendary1, "TRIGGER_SYNERGY", "LEGENDARY"),
                    })))),
            ]);

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
            Perk(Epic1, "ECONOMY", "EPIC"),
            Perk(Legendary1, "TRIGGER_SYNERGY", "LEGENDARY"),
        })));

    private static ContentValue Perk(string id, string category, string rarity) => Obj(
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
        ("requires", ContentValue.Array(Array.Empty<ContentValue>())),
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
