using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Core.Tests.Content;

/// <summary>Purpose-built event-card-shaped fixtures, one per thing the event resolver has to do.</summary>
/// <remarks>
/// Every id here is <c>EVT_FIXTURE_*</c>, which no shipped card uses — a fixture borrowing a real
/// card's id would read as a claim about that card, and a content edit to it would break tests that
/// were never about it.
/// </remarks>
internal static class FixtureCards
{
    /// <summary>A card whose one option pays flat GOLD — the run-scoped currency branch.</summary>
    internal const string Gold = "EVT_FIXTURE_GOLD";

    /// <summary>A card whose one option pays chapter-scaled CROWNS — the Player-wallet branch.</summary>
    internal const string Crowns = "EVT_FIXTURE_CROWNS";

    /// <summary>A card whose one option costs GOLD before it pays — the affordability branch.</summary>
    internal const string Costly = "EVT_FIXTURE_COSTLY";

    /// <summary>A card whose one option costs a META wallet currency rather than GOLD.</summary>
    internal const string CostlyMeta = "EVT_FIXTURE_COSTLY_META";

    /// <summary>A card whose options heal and hurt by a share of Max HP.</summary>
    internal const string Vitals = "EVT_FIXTURE_VITALS";

    /// <summary>A card whose one option pays a curse's reward.</summary>
    internal const string Curse = "EVT_FIXTURE_CURSE";

    /// <summary>A card whose one option does nothing at all — NONE and UNSUPPORTED.</summary>
    internal const string Inert = "EVT_FIXTURE_INERT";

    /// <summary>
    /// A card whose one option splits 70/30 — the weighted-outcome walk's own boundary.
    /// </summary>
    /// <remarks>
    /// The two branches pay <b>different</b> currencies rather than different amounts of one, so a
    /// test can name which branch resolved without arithmetic.
    /// </remarks>
    internal const string Split = "EVT_FIXTURE_SPLIT";

    /// <summary>A card available only in chapter 5 — the chapter-band filter's own subject.</summary>
    internal const string LateOnly = "EVT_FIXTURE_LATE_ONLY";

    /// <summary>A card whose two options grant fixed-die choices — one die, then two.</summary>
    /// <remarks>
    /// Two options with DIFFERENT counts, so a case can tell the authored number from a hard-coded
    /// one: an implementation granting a flat single choice satisfies the first option forever.
    /// </remarks>
    internal const string FixedDice = "EVT_FIXTURE_FIXED_DICE";

    /// <summary>Every fixture card, in a fixed order the draw tests can index against.</summary>
    internal static IReadOnlyList<ContentValue> All { get; } =
    [
        Card(Gold, 1, 8, Option("Take the gold", null, Outcome(1, Currency("GOLD", 100, false)))),
        Card(Crowns, 1, 8, Option("Take the crowns", null, Outcome(1, Currency("CROWNS", 40, true)))),
        Card(
            Costly,
            1,
            8,
            Option("Pay 100 Gold", Cost("GOLD", 100), Outcome(1, Currency("CROWNS", 40, true))),
            Option("Walk away", null, Outcome(1, None()))),
        Card(
            CostlyMeta,
            1,
            8,
            Option("Pay 6 Enhance Stones", Cost("ENHANCE_STONES", 6), Outcome(1, Currency("GOLD", 500, false))),
            Option("Walk away", null, Outcome(1, None()))),
        Card(
            Vitals,
            1,
            8,
            Option("Drink", null, Outcome(1, HpPct(0.25m))),
            Option("Bleed", null, Outcome(1, HpPct(-0.10m))),
            Option("Bleed out", null, Outcome(1, HpPct(-1.0m)))),
        Card(Curse, 1, 8, Option("Break the mirror", null, Outcome(1, CurseReward("CUR_MARKED")))),
        Card(
            Inert,
            1,
            8,
            Option("Leave it", null, Outcome(1, None())),
            Option("Move forward", null, Outcome(1, Unsupported("board movement is M3-02's")))),
        Card(
            Split,
            1,
            8,
            Option(
                "Reach in",
                null,
                Outcome(70, HpPct(-0.10m), Currency("ENHANCE_STONES", 3, true)),
                Outcome(30, HpPct(-0.10m), Currency("MERGE_DUST", 5, true)))),
        Card(LateOnly, 5, 5, Option("Nothing happens", null, Outcome(1, None()))),
        Card(
            FixedDice,
            1,
            8,
            Option("Take one die", null, Outcome(1, FixedDie(1))),
            Option("Take two", null, Outcome(1, FixedDie(2)))),
    ];

    internal static ContentValue Card(string id, int minChapter, int maxChapter, params ContentValue[] options) =>
        InRunIncomeDocuments.Obj(
            ("id", ContentValue.Text(id)),
            ("title", ContentValue.Text(id + " title")),
            ("body", ContentValue.Text(id + " body")),
            ("minChapter", ContentValue.Number(minChapter)),
            ("maxChapter", ContentValue.Number(maxChapter)),
            ("options", ContentValue.Array(options)));

    internal static ContentValue Option(string label, ContentValue? cost, params ContentValue[] outcomes)
    {
        var members = new List<(string, ContentValue)> { ("label", ContentValue.Text(label)) };

        if (cost is not null)
        {
            members.Add(("cost", cost));
        }

        members.Add(("outcomes", ContentValue.Array(outcomes)));

        return InRunIncomeDocuments.Obj(members.ToArray());
    }

    internal static ContentValue Cost(string currency, long amount) =>
        InRunIncomeDocuments.Obj(
            ("currency", ContentValue.Text(currency)),
            ("amount", ContentValue.Number(amount)));

    internal static ContentValue Outcome(decimal weight, params ContentValue[] effects) =>
        InRunIncomeDocuments.Obj(
            ("w", ContentValue.Number(weight)),
            ("effects", ContentValue.Array(effects)));

    internal static ContentValue Currency(string currency, long amount, bool chapterScaled) =>
        InRunIncomeDocuments.Obj(
            ("op", ContentValue.Text("CURRENCY")),
            ("currency", ContentValue.Text(currency)),
            ("amount", ContentValue.Number(amount)),
            ("chapterScaled", ContentValue.Boolean(chapterScaled)));

    internal static ContentValue HpPct(decimal amount) =>
        InRunIncomeDocuments.Obj(
            ("op", ContentValue.Text("HP_PCT")),
            ("amount", ContentValue.Number(amount)));

    internal static ContentValue CurseReward(string curseId) =>
        InRunIncomeDocuments.Obj(
            ("op", ContentValue.Text("CURSE_REWARD")),
            ("curseId", ContentValue.Text(curseId)));

    internal static ContentValue None() =>
        InRunIncomeDocuments.Obj(("op", ContentValue.Text("NONE")));

    internal static ContentValue Unsupported(string note) =>
        InRunIncomeDocuments.Obj(
            ("op", ContentValue.Text("UNSUPPORTED")),
            ("note", ContentValue.Text(note)));

    internal static ContentValue FixedDie(int count) =>
        InRunIncomeDocuments.Obj(
            ("op", ContentValue.Text("FIXED_DIE")),
            ("count", ContentValue.Number(count)));
}
