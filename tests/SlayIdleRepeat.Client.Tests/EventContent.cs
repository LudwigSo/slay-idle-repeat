using SlayIdleRepeat.Client.Game.Presenters;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Tests;

/// <summary>
/// The Event screen's string keys, and the content sets its cases run against.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>The cards are authored WHOLE.</b> <c>EventCatalogue.Read</c> validates every card it
/// reads — chapter band, at least one option, at least one outcome per option, a positive weight,
/// a known effect op — so a fixture carrying only ids and costs would make every case throw on a
/// malformed catalogue rather than exercise the screen. That is the difference between this holder
/// and <c>BoardContent.AuthoringEventCard</c>, which only ever had to satisfy a cost-or-not read.
/// </para>
/// <para>
/// 🔒 <b>Nothing here transcribes a shipped card.</b> Every id is <c>EVT_SCREEN_*</c>, which `19`
/// Part A does not use: a fixture borrowing a real card's id would read as a claim about that card,
/// and a content re-authoring pass would break client cases that were never about it.
/// </para>
/// <para>
/// Fixture values are the key with a marker in front, so every string is unique, obviously not the
/// shipped copy, and impossible for a hard-coded literal to match by accident.
/// </para>
/// </remarks>
internal static class EventContent
{
    internal const string TitleNameKey = "loc.event_screen.title.name";
    internal const string CostLabelKey = "loc.event_screen.cost.label";
    internal const string ResultLabelKey = "loc.event_screen.result.label";
    internal const string GoldLabelKey = "loc.event_screen.gold.label";
    internal const string HpLabelKey = "loc.event_screen.hp.label";
    internal const string FixedDiceLabelKey = "loc.event_screen.fixed_dice.label";
    internal const string WalletLabelKey = "loc.event_screen.wallet.label";
    internal const string ContinueActionKey = "loc.event_screen.continue.action";
    internal const string UnaffordableBlockKey = "loc.event_screen.unaffordable.block";
    internal const string LoadingStatusKey = "loc.event_screen.loading.status";
    internal const string DrawingStatusKey = "loc.event_screen.drawing.status";
    internal const string RunMissingStatusKey = "loc.event_screen.run_missing.status";
    internal const string NotAtAnEventStatusKey = "loc.event_screen.not_at_an_event.status";
    internal const string ReadUnavailableStatusKey = "loc.event_screen.read_unavailable.status";
    internal const string CardUnavailableStatusKey = "loc.event_screen.card_unavailable.status";
    internal const string NothingHappenedStatusKey = "loc.event_screen.nothing_happened.status";
    internal const string RefusedStatusKey = "loc.event_screen.refused.status";
    internal const string HostUnavailableStatusKey = "loc.event_screen.host_unavailable.status";

    /// <summary>
    /// 🔒 The currency captions, which are <c>tuning/currencies.json</c>'s and NOT this screen's.
    /// </summary>
    /// <remarks>
    /// Listed here because the fixture has to carry them for a priced option or a result row to
    /// resolve at all. A fixture that invented a <c>loc.event_screen.gold_currency.*</c> would be
    /// proving the screen against a key the content set does not have.
    /// </remarks>
    internal const string CurrencyGoldNameKey = "loc.currency.gold.name";

    internal const string CurrencyEnhanceStonesNameKey = "loc.currency.enhance_stones.name";

    /// <summary>Where the card catalogue lives — the document the draw reads.</summary>
    internal const string BoardEventsDocument = "content/board_events/board_events.json";

    /// <summary>A card whose one option is free and pays flat Gold.</summary>
    internal const string FreeCard = "EVT_SCREEN_FREE";

    /// <summary>A card whose first option costs 100 Gold and whose second is free.</summary>
    /// <remarks>
    /// 100 exactly, because the affordability boundary is <c>balance &gt;= cost</c> and the two
    /// cases that matter are 99 and 100. A round number no other fixture card uses, so an assertion
    /// naming it cannot be reading some other card's price.
    /// </remarks>
    internal const string PricedCard = "EVT_SCREEN_PRICED";

    /// <summary>A card whose first option costs a WALLET currency rather than Gold.</summary>
    /// <remarks>
    /// 🔒 The whole point of the second cost card. <c>EVENT_CHOOSE</c> checks Gold against the RUN
    /// and every other currency against the PLAYER, so a screen that read one balance for both
    /// would price this option off a purse the handler never looks at — and with the run's Gold at
    /// zero and the wallet funded, the two answers are opposite.
    /// </remarks>
    internal const string WalletCard = "EVT_SCREEN_WALLET";

    /// <summary>A card with THREE options, so an index that is not the pressed one is visible.</summary>
    /// <remarks>
    /// Three rather than two: with two options a screen that submitted "the other one" and one that
    /// submitted "the last one" are indistinguishable, and index 2 is the first index a two-option
    /// card cannot produce at all.
    /// </remarks>
    internal const string ThreeOptionCard = "EVT_SCREEN_THREE";

    /// <summary>The Gold the priced card's first option charges.</summary>
    internal const long PricedCardGoldCost = 100L;

    /// <summary>The Enhance Stones the wallet card's first option charges.</summary>
    internal const long WalletCardStoneCost = 6L;

    private static readonly ContentVersion FixtureStamp =
        ContentVersion.FromHex(new string('e', ContentVersion.HexLength));

    /// <summary>Every string key the Event screen renders, card prose aside.</summary>
    /// <remarks>
    /// 🔴 Card prose is deliberately absent: a card's title, body and option labels are free
    /// English in <c>board_events.json</c> rather than loc keys, so there is nothing here to resolve
    /// them through and a fixture that invented keys for them would prove the screen against a
    /// localisation the content set does not have.
    /// </remarks>
    internal static IReadOnlyList<string> EventKeys { get; } =
    [
        TitleNameKey,
        CostLabelKey, ResultLabelKey, GoldLabelKey, HpLabelKey, FixedDiceLabelKey, WalletLabelKey,
        ContinueActionKey,
        UnaffordableBlockKey,
        LoadingStatusKey, DrawingStatusKey, RunMissingStatusKey, NotAtAnEventStatusKey,
        ReadUnavailableStatusKey, CardUnavailableStatusKey, NothingHappenedStatusKey,
        RefusedStatusKey, HostUnavailableStatusKey,
    ];

    /// <summary>The currency captions the screen borrows rather than authoring its own.</summary>
    internal static IReadOnlyList<string> CurrencyNameKeys { get; } =
        [CurrencyGoldNameKey, CurrencyEnhanceStonesNameKey];

    /// <summary>The English fixture value for a key.</summary>
    internal static string EnglishValueOf(string key) => "FIXTURE " + key;

    /// <summary>The German fixture value for a key — a marker, not a translation.</summary>
    internal static string GermanValueOf(string key) => "FIXTURE-DE " + key;

    /// <summary>A catalogue answering in English over a content set.</summary>
    internal static LocaleStringCatalogue Catalogue(ContentSnapshot content) =>
        new(content, ScreenContent.English);

    /// <summary>A content set carrying the screen's strings and no card catalogue at all.</summary>
    /// <remarks>
    /// The shape a content set stripped of the document the projection depends on actually has —
    /// which is what the <c>CardUnavailable</c> arm has to be a sentence about rather than a crash.
    /// </remarks>
    internal static ContentSnapshot Strings() => new(FixtureStamp, [.. Locales()]);

    /// <summary>A content set carrying the strings and the four fixture cards.</summary>
    internal static ContentSnapshot AuthoringCards()
    {
        var documents = new List<ContentDocument>(Locales()) { Cards() };

        return new ContentSnapshot(FixtureStamp, documents);
    }

    /// <summary>The card catalogue, authored whole.</summary>
    private static ContentDocument Cards() =>
        new(BoardEventsDocument, Obj(
            ("cards", ContentValue.Array(
            [
                Card(
                    FreeCard,
                    Option("Take the purse", cost: null, Outcome(Currency("GOLD", 300, false)))),
                Card(
                    PricedCard,
                    Option(
                        "Pay the toll",
                        Cost("GOLD", PricedCardGoldCost),
                        Outcome(Currency("CROWNS", 40, true))),
                    Option("Turn back", cost: null, Outcome(None()))),
                Card(
                    WalletCard,
                    Option(
                        "Offer the stones",
                        Cost("ENHANCE_STONES", WalletCardStoneCost),
                        Outcome(Currency("GOLD", 500, false))),
                    Option("Keep them", cost: null, Outcome(None()))),
                Card(
                    ThreeOptionCard,
                    Option("Left door", cost: null, Outcome(Currency("GOLD", 10, false))),
                    Option("Middle door", cost: null, Outcome(HpPct(-0.10m))),
                    Option("Right door", cost: null, Outcome(FixedDie(1)))),
            ]))));

    /// <summary>One whole card: a chapter band every fixture run is inside, and its options.</summary>
    private static ContentValue Card(string id, params ContentValue[] options) =>
        Obj(
            ("id", ContentValue.Text(id)),
            ("title", ContentValue.Text(id + " title")),
            ("body", ContentValue.Text(id + " body")),
            ("minChapter", ContentValue.Number(1)),
            ("maxChapter", ContentValue.Number(8)),
            ("options", ContentValue.Array(options)));

    private static ContentValue Option(string label, ContentValue? cost, params ContentValue[] outcomes)
    {
        var members = new List<(string, ContentValue)> { ("label", ContentValue.Text(label)) };

        if (cost is not null)
        {
            members.Add(("cost", cost));
        }

        members.Add(("outcomes", ContentValue.Array(outcomes)));

        return Obj(members.ToArray());
    }

    private static ContentValue Cost(string currency, long amount) =>
        Obj(("currency", ContentValue.Text(currency)), ("amount", ContentValue.Number(amount)));

    /// <summary>One outcome at weight 1 — every fixture option has exactly one, so nothing rolls.</summary>
    private static ContentValue Outcome(params ContentValue[] effects) =>
        Obj(("w", ContentValue.Number(1)), ("effects", ContentValue.Array(effects)));

    private static ContentValue Currency(string currency, long amount, bool chapterScaled) =>
        Obj(
            ("op", ContentValue.Text("CURRENCY")),
            ("currency", ContentValue.Text(currency)),
            ("amount", ContentValue.Number(amount)),
            ("chapterScaled", ContentValue.Boolean(chapterScaled)));

    private static ContentValue HpPct(decimal amount) =>
        Obj(("op", ContentValue.Text("HP_PCT")), ("amount", ContentValue.Number(amount)));

    private static ContentValue FixedDie(int count) =>
        Obj(("op", ContentValue.Text("FIXED_DIE")), ("count", ContentValue.Number(count)));

    private static ContentValue None() => Obj(("op", ContentValue.Text("NONE")));

    private static ContentValue Obj(params (string Name, ContentValue Value)[] members) =>
        ContentValue.Object(members.Select(m =>
            new KeyValuePair<string, ContentValue>(m.Name, m.Value)));

    private static IReadOnlyList<ContentDocument> Locales()
    {
        var keys = EventKeys.Concat(CurrencyNameKeys).ToArray();

        return
        [
            Locale("loc/en.json", ScreenContent.English, keys, EnglishValueOf),
            Locale("loc/de.json", ScreenContent.German, keys, GermanValueOf),
        ];
    }

    private static ContentDocument Locale(
        string path, string localeTag, IReadOnlyList<string> keys, Func<string, string> valueOf) =>
        new(path, ContentValue.Object(
        [
            new KeyValuePair<string, ContentValue>("_locale", ContentValue.Text(localeTag)),
            new KeyValuePair<string, ContentValue>("strings", ContentValue.Object(
                keys.Select(k =>
                    new KeyValuePair<string, ContentValue>(k, ContentValue.Text(valueOf(k)))))),
        ]));
}
