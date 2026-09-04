using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The Minigame screen's strings as content: the document that names them, the schema that governs
/// it, and the pairing row that has to exist alongside both.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 A loc key that nothing in the content set names is an <c>OrphanedReference</c>, and every issue
/// is fatal — so a screen whose strings were referenced only from C# would fail the content load and
/// take the game with it. <c>content/minigame/minigame.json</c> is what names them, and these cases
/// are what stop it being quietly deleted or emptied.
/// </para>
/// <para>
/// ⚠️ The directory is <c>minigame/</c>, matching the <c>loc.minigame.*</c> prefix the screen
/// resolves — the same directory-matching convention <c>campfire/</c> and <c>event_screen/</c> use.
/// It sits beside <c>tuning/minigames.json</c>, which is a different document with a different
/// schema and a plural name; the pairing table row is what keeps the two apart.
/// </para>
/// <para>
/// ⚠️ The member names below are the shape the document must be authored in, because the orphan case
/// anchors on one of them as literal text. Two-space indentation and one space after the colon,
/// exactly as <c>content/campfire/campfire.json</c> is authored.
/// </para>
/// </remarks>
public sealed class MinigameDataTests
{
    private const string MinigameDocument = "content/minigame/minigame.json";

    private const string MinigameSchema = "schema/minigame.schema.json";

    private const string MinigameDirectory = "content/minigame/";

    private const string TitleNameKey = "loc.minigame.title.name";

    private const string ContinueActionKey = "loc.minigame.continue.action";

    private const string GuaranteeLabelKey = "loc.minigame.guarantee.label";

    private const string AlreadyResolvedStatusKey = "loc.minigame.already_resolved.status";

    private const string RefusedStatusKey = "loc.minigame.refused.status";

    private const string HostUnavailableStatusKey = "loc.minigame.host_unavailable.status";

    private const string ReadUnavailableStatusKey = "loc.minigame.read_unavailable.status";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    /// <summary>The prefix the eight currency captions live under, which this document may not own.</summary>
    private const string CurrencyKeyPrefix = "loc.currency.";

    /// <summary>One of the eight <c>tuning/currencies.json</c> already owns.</summary>
    private const string CurrencyNameKeyAlreadyNamedByTuning = "loc.currency.beast_feed.name";

    /// <summary>
    /// Every string the Minigame screen renders.
    /// </summary>
    /// <remarks>
    /// 🔒 Four of the arms are authored for all FOUR minigames although only three have a screen.
    /// The rules layer accepts a submission for the fourth, its outcome tokens are in the reward
    /// table, and the outcome captions are keyed off those tokens — so leaving the fourth arm's name
    /// and rule unauthored would leave the one arm that can still be reached through the rules layer
    /// as the one arm with no words.
    /// </remarks>
    private static readonly string[] Keys =
    [
        TitleNameKey,
        "loc.minigame.chest_pick.name",
        "loc.minigame.timing_bar.name",
        "loc.minigame.dice_duel.name",
        "loc.minigame.memory_rune.name",
        "loc.minigame.chest_pick.rule",
        "loc.minigame.timing_bar.rule",
        "loc.minigame.dice_duel.rule",
        "loc.minigame.memory_rune.rule",
        "loc.minigame.rewards.label",
        "loc.minigame.hits.label",
        "loc.minigame.strikes_left.label",
        GuaranteeLabelKey,
        "loc.minigame.result.label",
        "loc.minigame.strike.action",
        "loc.minigame.step.action",
        "loc.minigame.pick_chest.action",
        "loc.minigame.roll.action",
        ContinueActionKey,
        "loc.minigame.bronze.outcome",
        "loc.minigame.silver.outcome",
        "loc.minigame.gold.outcome",
        "loc.minigame.hits_0.outcome",
        "loc.minigame.hits_1.outcome",
        "loc.minigame.hits_2.outcome",
        "loc.minigame.hits_3.outcome",
        "loc.minigame.loss.outcome",
        "loc.minigame.win_2_1.outcome",
        "loc.minigame.win_2_0.outcome",
        "loc.minigame.fail_round_1.outcome",
        "loc.minigame.round_1_only.outcome",
        "loc.minigame.both_rounds.outcome",
        "loc.minigame.loading.status",
        "loc.minigame.playing.status",
        "loc.minigame.run_missing.status",
        "loc.minigame.not_at_a_minigame.status",
        ReadUnavailableStatusKey,
        "loc.minigame.rules_unavailable.status",
        AlreadyResolvedStatusKey,
        RefusedStatusKey,
        HostUnavailableStatusKey,
    ];

    /// <summary>
    /// The loaded shipped set, read once for the whole class.
    /// </summary>
    /// <remarks>
    /// The sweep below runs one row per string this screen renders, and loading the whole
    /// <c>game-data</c> tree per row costs minutes of wall clock for an answer that cannot change
    /// between them — the snapshot is immutable, so sharing it changes nothing but the time.
    /// </remarks>
    private static readonly Lazy<ContentSnapshot> LazyShipped =
        new(() => ContentLoader.Load(RepoData.Source()).Require());

    private static ContentSnapshot Shipped => LazyShipped.Value;

    /// <summary>How many strings the screen renders, so a sweep over an empty list cannot pass.</summary>
    private const int ExpectedKeyCount = 41;

    public static TheoryData<string> EveryKey
    {
        get
        {
            var rows = new TheoryData<string>();

            foreach (var key in Keys)
            {
                rows.Add(key);
            }

            return rows;
        }
    }

    /// <summary>
    /// 🔒 The list the sweep below runs over is the size this screen actually needs.
    /// </summary>
    /// <remarks>
    /// 🔴 A theory over a list is the shape that silently measures nothing. A key quietly dropped
    /// from the array leaves every remaining row green and the missing string renders as its own
    /// dotted identifier on a handset.
    /// </remarks>
    [Fact]
    public void The_screen_renders_the_number_of_strings_this_suite_sweeps()
    {
        Keys.Length.ShouldBe(
            ExpectedKeyCount,
            "the Minigame screen's string list changed size. Adding or removing a caption is a real " +
            "change and belongs in both locales and the document — decide it here, deliberately.");
        Keys.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            Keys.Length, "a key is listed twice, so the sweep covers fewer strings than it counts.");
    }

    [Fact]
    public void The_minigame_document_pairs_with_the_minigame_schema()
    {
        var shipped = RepoData.Documents.Keys;

        ContentLayout.SchemaFor(MinigameDocument).ShouldBe(
            MinigameSchema,
            "an unpaired data file is one nobody validates. This is the pairing the loader resolves, " +
            "and it is the reason a malformed minigame document fails the build rather than the screen.");
        shipped.ShouldContain(
            MinigameDocument,
            "and the pairing is only real if the document is. SchemaFor is a pure transform over a " +
            "string — it answers just as confidently for a path nobody ever authored.");
        shipped.ShouldContain(
            MinigameSchema,
            "likewise the schema half: deleting schema/minigame.schema.json leaves SchemaFor " +
            "answering exactly as it does today, and this is the assertion that notices.");
    }

    /// <summary>
    /// 🔒 The declared row, not the answer the stem rule happens to give.
    /// </summary>
    /// <remarks>
    /// The hazard here is a plural: <c>tuning/minigames.json</c> pairs with
    /// <c>schema/minigames.schema.json</c>, one letter away from this screen's own schema. A
    /// directory row is what keeps the two from resolving into each other.
    /// </remarks>
    [Fact]
    public void The_minigame_directory_is_declared_in_the_content_type_pairing_table()
    {
        ContentLayout.ContentTypeSchemas.ShouldContain(
            new KeyValuePair<string, string>(MinigameDirectory, MinigameSchema),
            "content pairs by DIRECTORY, and the table is the declaration. Leaving this row out " +
            "leans on the stem rule agreeing by coincidence — and this stem is one letter from the " +
            "timing bar's own tuning schema, so the coincidence is worth less here than usual.");
    }

    /// <summary>The consequence of the row above, stated as the behaviour it buys.</summary>
    [Fact]
    public void A_second_document_in_the_minigame_directory_still_pairs_with_the_one_schema()
    {
        ContentLayout.SchemaFor("content/minigame/minigame_outcomes.json").ShouldBe(
            MinigameSchema,
            "a content directory holds many files of one TYPE, all governed by that type's single " +
            "schema. Under the bare stem rule this would demand " +
            "schema/minigame_outcomes.schema.json and fail as MissingSchema — a build break landing " +
            "on whoever adds the file, for a pairing decision made here.");
    }

    /// <summary>
    /// 🔒 The document is there, <b>and then</b> the whole set still loads with it in.
    /// </summary>
    [Fact]
    public void The_shipped_data_set_still_validates_with_the_minigame_document_present()
    {
        RepoData.Documents.Keys.ShouldContain(
            MinigameDocument,
            "the document is absent, so the load below succeeds without ever seeing this screen and " +
            "this case's name is a claim about a file nobody shipped.");

        ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical).Succeeded.ShouldBeTrue(
            "an unreferenced loc key is fatal. If this is red, the minigame strings were added to " +
            "the locales without the document that names them — a client that cannot load its own " +
            "content.");
    }

    [Theory]
    [MemberData(nameof(EveryKey))]
    public void Every_minigame_string_the_screen_shows_is_carried_by_both_locales(string key)
    {
        var snapshot = Shipped;

        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' is a string the Minigame screen renders, and X-04 requires every user-facing " +
            "string to be a key in EN and DE from day one. A missing one renders as its own key on " +
            "a screen whose every word is chrome — there is no authored prose here to carry it.");
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
    /// 🔴 Five unrelated situations with five different next actions: the tile has already been
    /// played, the read never answered, the content set cannot describe the game, the rules layer
    /// refused the submission, and the host did not answer at all. Sharing wording between any two
    /// tells the player one of these is the other — and on a screen where the only control is
    /// Continue, the sentence is the entire explanation.
    /// </remarks>
    [Fact]
    public void The_sentences_this_screen_adds_read_as_different_sentences()
    {
        var snapshot = Shipped;

        string?[] sentences =
        [
            snapshot.ReadText(EnglishStrings + AlreadyResolvedStatusKey),
            snapshot.ReadText(EnglishStrings + ReadUnavailableStatusKey),
            snapshot.ReadText(EnglishStrings + "loc.minigame.rules_unavailable.status"),
            snapshot.ReadText(EnglishStrings + RefusedStatusKey),
            snapshot.ReadText(EnglishStrings + HostUnavailableStatusKey),
            snapshot.ReadText(EnglishStrings + ContinueActionKey),
        ];

        sentences.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            sentences.Length,
            "two of this screen's own sentences are AUTHORED the same. A tile already played, a read " +
            "that never came back, a content set that cannot describe the game, a rules refusal and " +
            "a host that never answered are five different situations, and the wording is the only " +
            $"thing telling any of them apart: [{string.Join(" | ", sentences)}]");
    }

    /// <summary>
    /// 🔒 The eight currency names stay <c>tuning/currencies.json</c>'s, and this says so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The reward rows list Gold, Crowns, Beast Feed and Enhance Stones, so the temptation is a
    /// caption per currency in this document — which would leave two documents claiming one key and
    /// the second free to drift out of step with the wallet it describes.
    /// </para>
    /// <para>
    /// 🔴 <b>Checked over the document's string VALUES rather than its raw text</b>, deliberately
    /// unlike the sibling case on the Event screen. That one reads the file as one string and cannot
    /// tell a key from prose, so a <c>_doc</c> explaining which currency captions the screen borrows
    /// trips it — an authoring trap that has already cost this branch a rewrite. Here the note may
    /// say whatever it needs to and only an actual authored value can fail.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_currency_names_are_already_carried_without_this_document_naming_them()
    {
        var snapshot = Shipped;

        snapshot.ReadText(EnglishStrings + CurrencyNameKeyAlreadyNamedByTuning)
                .ShouldNotBeNullOrWhiteSpace(
                    "tuning/currencies.json names the eight currencies, and this screen's reward " +
                    "rows are captioned through those keys. If this is red the tuning file has " +
                    "stopped naming them, and the minigame document is not the place to start.");

        RepoData.Documents.Keys.ShouldContain(
            MinigameDocument,
            "with no document there is nothing to check for a borrowed currency caption, and this " +
            "case would report a clean separation it never looked at.");

        var borrowed = AuthoredValues()
            .Where(value => value.StartsWith(CurrencyKeyPrefix, StringComparison.Ordinal))
            .ToArray();

        borrowed.ShouldBeEmpty(
            "the minigame document authors a currency caption of its own. Those eight keys belong " +
            "to tuning/currencies.json, which is also what decides what a currency IS — a caption " +
            $"authored here would be a second answer to a question that already has one: [{string.Join(", ", borrowed)}]");
    }

    /// <summary>
    /// 🔒 The guard proved to bite. Every other case here passes hardest when the orphan rule has
    /// stopped running, so one case has to break the reference on purpose.
    /// </summary>
    [Fact]
    public void A_minigame_string_the_document_stops_naming_is_rejected_as_an_orphan()
    {
        var source = RepoData.SourceWithEdit(
            MinigameDocument,
            $"\"continue\": \"{ContinueActionKey}\"",
            $"\"continue\": \"{TitleNameKey}\"");

        ContentLoader.Load(source).Issues.ShouldContain(
            i => i.Code == ContentIssueCode.OrphanedReference &&
                 i.Location == EnglishStrings + ContinueActionKey,
            "with nothing naming it, the Continue caption becomes a translation somebody pays for " +
            "twice — and, because every issue is fatal, a client that will not load. Continue is " +
            "also the only control on this screen once the game has resolved, so its caption going " +
            "unnamed leaves the one way off the tile drawn as a raw key.");
    }

    /// <summary>Every string this document authors as a value, wherever it sits in the tree.</summary>
    private static IReadOnlyList<string> AuthoredValues()
    {
        using var document = JsonDocument.Parse(RepoData.Documents[MinigameDocument]);

        var values = new List<string>();

        Collect(document.RootElement, values);

        return values;
    }

    private static void Collect(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in element.EnumerateObject())
                {
                    // The note is prose about the document rather than a string the screen renders,
                    // and it is allowed to explain which captions this screen borrows.
                    if (!member.Name.StartsWith('_') && !member.NameEquals("$schema"))
                    {
                        Collect(member.Value, values);
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Collect(item, values);
                }

                break;

            case JsonValueKind.String:
                values.Add(element.GetString()!);

                break;

            default:
                break;
        }
    }
}
