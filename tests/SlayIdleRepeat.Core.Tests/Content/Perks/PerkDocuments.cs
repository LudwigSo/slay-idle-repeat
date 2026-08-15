using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content.Perks;

/// <summary>
/// Hermetic <c>content/perks/perks.json</c> fixtures — a small catalogue spanning every `06` §4
/// rarity band and more than one `06` §2 category, enough to exercise <c>PerkCatalogue</c> and the
/// M3-06 draft engine without depending on the shipped 46-row set.
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

    /// <summary>The document, for composing into a larger fixture snapshot alongside it.</summary>
    internal static ContentDocument Document { get; } = new(DocumentPath, Root());

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
