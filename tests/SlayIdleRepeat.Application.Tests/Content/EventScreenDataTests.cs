using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The Event screen's strings as content: the document that names them, the schema that governs it,
/// and the pairing row that has to exist alongside both.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 A loc key that nothing in the content set names is an <c>OrphanedReference</c>, and every
/// issue is fatal — so a screen whose strings were referenced only from C# would fail the content
/// load and take the game with it. <c>content/event_screen/event_screen.json</c> is what names them,
/// and these cases are what stop it being quietly deleted or emptied.
/// </para>
/// <para>
/// ⚠️ <b>The directory is <c>event_screen/</c> and not <c>event/</c>, and that is deliberate.</b>
/// <c>schema/event.schema.json</c> already governs the live-ops event package and
/// <c>schema/events.schema.json</c> the framework-wide tuning file, and
/// <c>schema/board_events.schema.json</c> the cards this screen shows. Four schema names differing
/// by a letter and a word is the naming hazard the pairing table's own comment calls out, and the
/// screen's own document takes the one name none of the other three could be mistaken for.
/// </para>
/// <para>
/// 🔴 <b>The card prose is NOT here.</b> A card's <c>title</c>, <c>body</c> and option
/// <c>label</c> are free English strings in <c>board_events.json</c> rather than loc keys, so the
/// locale check never sees them and a German player reads the card in English until a content pass
/// re-authors them. What this document carries is the screen's own chrome, which is fully localised.
/// </para>
/// <para>
/// ⚠️ The member names below are the shape the document must be authored in, because the orphan
/// case anchors on one of them as literal text. Two-space indentation and one space after the
/// colon, exactly as <c>content/campfire/campfire.json</c> is authored.
/// </para>
/// </remarks>
public sealed class EventScreenDataTests
{
    private const string EventScreenDocument = "content/event_screen/event_screen.json";

    private const string EventScreenSchema = "schema/event_screen.schema.json";

    private const string EventScreenDirectory = "content/event_screen/";

    private const string TitleNameKey = "loc.event_screen.title.name";

    private const string ContinueActionKey = "loc.event_screen.continue.action";

    private const string UnaffordableBlockKey = "loc.event_screen.unaffordable.block";

    private const string NothingHappenedStatusKey = "loc.event_screen.nothing_happened.status";

    private const string RefusedStatusKey = "loc.event_screen.refused.status";

    private const string HostUnavailableStatusKey = "loc.event_screen.host_unavailable.status";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    /// <summary>One of the eight currency names <c>tuning/currencies.json</c> already owns.</summary>
    private const string CurrencyNameKeyAlreadyNamedByTuning = "loc.currency.enhance_stones.name";

    [Fact]
    public void The_event_screen_document_pairs_with_the_event_screen_schema()
    {
        var shipped = RepoData.Documents.Keys;

        ContentLayout.SchemaFor(EventScreenDocument).ShouldBe(
            EventScreenSchema,
            "an unpaired data file is one nobody validates. This is the pairing the loader resolves, " +
            "and it is the reason a malformed event-screen document fails the build rather than the " +
            "screen.");
        shipped.ShouldContain(
            EventScreenDocument,
            "and the pairing is only real if the document is. SchemaFor is a pure transform over a " +
            "string — it answers just as confidently for a path nobody ever authored, so the " +
            "assertion above passes unchanged against a checkout carrying neither file.");
        shipped.ShouldContain(
            EventScreenSchema,
            "likewise the schema half: deleting schema/event_screen.schema.json leaves SchemaFor " +
            "answering exactly as it does today, and this is the assertion that notices.");
    }

    /// <summary>
    /// 🔒 The declared row, not the answer the stem rule happens to give — the same easy case the
    /// pairing table's own remarks name: it answers correctly while there is exactly one file, and
    /// the gap only becomes visible as a wrongly-resolved path the day a second arrives.
    /// </summary>
    [Fact]
    public void The_event_screen_directory_is_declared_in_the_content_type_pairing_table()
    {
        ContentLayout.ContentTypeSchemas.ShouldContain(
            new KeyValuePair<string, string>(EventScreenDirectory, EventScreenSchema),
            "content pairs by DIRECTORY, and the table is the declaration. Leaving this row out " +
            "leans on the stem rule agreeing by coincidence — and this directory's stem is one " +
            "letter and one word away from three other shipped schema names, so the coincidence is " +
            "worth less here than anywhere else in the tree.");
    }

    /// <summary>The consequence of the row above, stated as the behaviour it buys.</summary>
    [Fact]
    public void A_second_document_in_the_event_screen_directory_still_pairs_with_the_one_schema()
    {
        ContentLayout.SchemaFor("content/event_screen/event_screen_outcomes.json").ShouldBe(
            EventScreenSchema,
            "a content directory holds many files of one TYPE, all governed by that type's single " +
            "schema. Under the bare stem rule this would demand " +
            "schema/event_screen_outcomes.schema.json and fail as MissingSchema — a build break " +
            "landing on whoever adds the file, for a pairing decision made here.");
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_the_event_screen_document_present()
    {
        var result = ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical);

        result.Succeeded.ShouldBeTrue(
            "an unreferenced loc key is fatal. If this is red, the event-screen strings were added " +
            "to the locales without the document that names them — which is a client that cannot " +
            "load its own content.");
    }

    [Theory]
    [InlineData(TitleNameKey)]
    [InlineData("loc.event_screen.cost.label")]
    [InlineData("loc.event_screen.result.label")]
    [InlineData("loc.event_screen.gold.label")]
    [InlineData("loc.event_screen.hp.label")]
    [InlineData("loc.event_screen.fixed_dice.label")]
    [InlineData("loc.event_screen.wallet.label")]
    [InlineData(ContinueActionKey)]
    [InlineData(UnaffordableBlockKey)]
    [InlineData("loc.event_screen.loading.status")]
    [InlineData("loc.event_screen.drawing.status")]
    [InlineData("loc.event_screen.run_missing.status")]
    [InlineData("loc.event_screen.not_at_an_event.status")]
    [InlineData("loc.event_screen.read_unavailable.status")]
    [InlineData("loc.event_screen.card_unavailable.status")]
    [InlineData(NothingHappenedStatusKey)]
    [InlineData(RefusedStatusKey)]
    [InlineData(HostUnavailableStatusKey)]
    public void Every_event_screen_string_the_screen_shows_is_carried_by_both_locales(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' is a string the Event screen renders, and X-04 requires every user-facing " +
            "string to be a key in EN and DE from day one. A missing one renders as its own key on " +
            "a screen whose chrome is the only localised text it has — the card prose beside it is " +
            "authored English either way.");
        snapshot.ReadText(GermanStrings + key).ShouldNotBe(
            snapshot.ReadText(EnglishStrings + key),
            "and the German file has to carry its own value rather than the English one copied " +
            "across. Stated as 'different from EN' rather than as any particular wording: the DE " +
            "values are untranslated placeholders today, and pinning their text would assert a " +
            "translation that has not been done.");
    }

    /// <summary>
    /// 🔒 The sentences this screen adds read as different sentences, <b>as authored</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 Four of these five are what a player is told after pressing something. "You cannot afford
    /// this", "nothing happened", "the rules refused that" and "the game did not answer" are four
    /// unrelated situations with four different next actions, and most shipped event outcomes are
    /// <c>UNSUPPORTED</c> — so the nothing-happened sentence is the COMMON one, not an edge case.
    /// Sharing wording between any two of them tells the player one of these is the other.
    /// </remarks>
    [Fact]
    public void The_sentences_this_screen_adds_read_as_different_sentences()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        string?[] sentences =
        [
            snapshot.ReadText(EnglishStrings + UnaffordableBlockKey),
            snapshot.ReadText(EnglishStrings + NothingHappenedStatusKey),
            snapshot.ReadText(EnglishStrings + RefusedStatusKey),
            snapshot.ReadText(EnglishStrings + HostUnavailableStatusKey),
            snapshot.ReadText(EnglishStrings + ContinueActionKey),
        ];

        sentences.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            sentences.Length,
            "two of this screen's own sentences are AUTHORED the same. An option priced out of " +
            "reach, an outcome that did nothing, a refusal the rules layer sent back and a host " +
            "that never answered are four different situations, and the wording is the only thing " +
            $"telling any of them apart: [{string.Join(" | ", sentences)}]");
    }

    /// <summary>
    /// 🔒 The eight currency names stay <c>tuning/currencies.json</c>'s, and this says so.
    /// </summary>
    /// <remarks>
    /// The screen prices options in whichever currency a card charges and lists what moved, so the
    /// temptation is to author a caption per currency in this document — which would leave two
    /// documents claiming one key and the second free to drift out of step with the wallet it
    /// describes.
    /// </remarks>
    [Fact]
    public void The_currency_names_are_already_carried_without_this_document_naming_them()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + CurrencyNameKeyAlreadyNamedByTuning)
                .ShouldNotBeNullOrWhiteSpace(
                    "tuning/currencies.json names the eight currencies, and this screen prices its " +
                    "options and lists its result rows through those keys. If this is red the " +
                    "tuning file has stopped naming them, and the event-screen document is not the " +
                    "place to start.");
    }

    /// <summary>
    /// 🔒 The guard proved to bite. Every other case here passes hardest when the orphan rule has
    /// stopped running, so one case has to break the reference on purpose.
    /// </summary>
    [Fact]
    public void An_event_screen_string_the_document_stops_naming_is_rejected_as_an_orphan()
    {
        var source = RepoData.SourceWithEdit(
            EventScreenDocument,
            $"\"continue\": \"{ContinueActionKey}\"",
            $"\"continue\": \"{TitleNameKey}\"");

        ContentLoader.Load(source).Issues.ShouldContain(
            i => i.Code == ContentIssueCode.OrphanedReference &&
                 i.Location == EnglishStrings + ContinueActionKey,
            "with nothing naming it, the Continue caption becomes a translation somebody pays for " +
            "twice — and, because every issue is fatal, a client that will not load. Continue is " +
            "also the only control on this screen once a choice has resolved, so its caption going " +
            "unnamed leaves the one way off the tile drawn as a raw key.");
    }
}
