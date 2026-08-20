using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The Campfire / Shrine screen's strings as content: the document that names them, the schema that
/// governs it, and the pairing row that has to exist alongside both.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 A loc key that nothing in the content set names is an <c>OrphanedReference</c>, and every
/// issue is fatal — so a screen whose strings were referenced only from C# would fail the content
/// load and take the game with it. <c>content/campfire/campfire.json</c> is what names them, and
/// these cases are what stop it being quietly deleted or emptied.
/// </para>
/// <para>
/// 🔴 <b>One document for two arms, because it is one screen with two arms.</b> A campfire and a
/// shrine are separate tile kinds that open the same screen, and the <c>block</c> group carries
/// four separate absences across them: the campfire's perk-tier upgrade and its reroll-charge grant (the latter now removed outright)
/// are refused for two different unbuilt reasons, the shrine's choice does not exist as a command
/// at all, and the shrine's Cleanse arm cannot fire because a run holds no curse list. Four
/// sentences, because a player told the wrong one goes looking for the wrong thing.
/// </para>
/// <para>
/// 🔒 <b>The ten shrine buff names are deliberately NOT here.</b> <c>tuning/currencies.json</c>
/// already names <c>loc.shrine.&lt;x&gt;.name</c> for all ten as the pool's own display names, so
/// they are referenced, non-orphaned and translated already. Naming them a second time would be two
/// documents claiming one key.
/// </para>
/// <para>
/// ⚠️ The member names below are the shape the document must be authored in, because the orphan
/// case anchors on one of them as literal text. Two-space indentation and one space after the
/// colon, exactly as <c>content/board/board.json</c> is authored.
/// </para>
/// </remarks>
public sealed class CampfireDataTests
{
    private const string CampfireDocument = "content/campfire/campfire.json";

    private const string CampfireSchema = "schema/campfire.schema.json";

    private const string CampfireDirectory = "content/campfire/";

    private const string RestActionKey = "loc.campfire.rest.action";

    private const string TitleNameKey = "loc.campfire.title.name";

    private const string UpgradePerkNoneBlockKey = "loc.campfire.upgrade_perk_none.block";

    private const string TakeActionKey = "loc.campfire.take.action";

    private const string CleanseActionKey = "loc.campfire.cleanse.action";

    private const string ShrineChooseLabelKey = "loc.campfire.shrine_choose.label";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    /// <summary>One of the ten buff names the shrine pool already owns, used as the control below.</summary>
    private const string ShrineBuffNameKeyAlreadyNamedByTuning = "loc.shrine.atk.name";

    [Fact]
    public void The_campfire_document_pairs_with_the_campfire_schema()
    {
        var shipped = RepoData.Documents.Keys;

        ContentLayout.SchemaFor(CampfireDocument).ShouldBe(
            CampfireSchema,
            "an unpaired data file is one nobody validates. This is the pairing the loader resolves, " +
            "and it is the reason a malformed campfire document fails the build rather than the screen.");
        shipped.ShouldContain(
            CampfireDocument,
            "and the pairing is only real if the document is. SchemaFor is a pure transform over a " +
            "string — it answers just as confidently for a path nobody ever authored, so the " +
            "assertion above passes unchanged against a checkout carrying neither file.");
        shipped.ShouldContain(
            CampfireSchema,
            "likewise the schema half: deleting schema/campfire.schema.json leaves SchemaFor " +
            "answering exactly as it does today, and this is the assertion that notices.");
    }

    /// <summary>
    /// 🔒 The declared row, not the answer the stem rule happens to give — the same easy case the
    /// pairing table's own remarks name: it answers correctly while there is exactly one file, and
    /// the gap only becomes visible as a wrongly-resolved path the day a second arrives.
    /// </summary>
    [Fact]
    public void The_campfire_directory_is_declared_in_the_content_type_pairing_table()
    {
        ContentLayout.ContentTypeSchemas.ShouldContain(
            new KeyValuePair<string, string>(CampfireDirectory, CampfireSchema),
            "content pairs by DIRECTORY, and the table is the declaration. Leaving this row out " +
            "leans on the stem rule agreeing by coincidence, which it does today and stops doing " +
            "the moment the shrine arm's own strings are split into a second document.");
    }

    /// <summary>The consequence of the row above, stated as the behaviour it buys.</summary>
    /// <remarks>
    /// 🔴 Not a hypothetical here. This screen already serves two arms out of one document, and
    /// splitting the shrine's half out is the obvious tidy-up — which under the bare stem rule would
    /// break the build for whoever did it.
    /// </remarks>
    [Fact]
    public void A_second_document_in_the_campfire_directory_still_pairs_with_the_one_schema()
    {
        ContentLayout.SchemaFor("content/campfire/campfire_shrine.json").ShouldBe(
            CampfireSchema,
            "a content directory holds many files of one TYPE, all governed by that type's single " +
            "schema. Under the bare stem rule this would demand schema/campfire_shrine.schema.json " +
            "and fail as MissingSchema — a build break landing on whoever adds the file, for a " +
            "pairing decision made here.");
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_the_campfire_document_present()
    {
        var result = ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical);

        result.Succeeded.ShouldBeTrue(
            "an unreferenced loc key is fatal. If this is red, the campfire strings were added to " +
            "the locales without the document that names them — which is a client that cannot load " +
            "its own content.");
    }

    [Theory]
    [InlineData(TitleNameKey)]
    [InlineData("loc.campfire.shrine_title.name")]
    [InlineData("loc.campfire.shrine_buffs.label")]
    [InlineData(RestActionKey)]
    [InlineData("loc.campfire.upgrade_perk.action")]
    [InlineData("loc.campfire.continue.action")]
    [InlineData(UpgradePerkNoneBlockKey)]
    [InlineData(TakeActionKey)]
    [InlineData(CleanseActionKey)]
    [InlineData(ShrineChooseLabelKey)]
    [InlineData("loc.campfire.loading.status")]
    [InlineData("loc.campfire.run_missing.status")]
    [InlineData("loc.campfire.not_at_a_campfire.status")]
    [InlineData("loc.campfire.read_unavailable.status")]
    [InlineData("loc.campfire.shrine_unavailable.status")]
    [InlineData("loc.campfire.refused.status")]
    [InlineData("loc.campfire.host_unavailable.status")]
    public void Every_campfire_string_the_screen_shows_is_carried_by_both_locales(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' is a string the Campfire / Shrine screen renders, and X-04 requires every " +
            "user-facing string to be a key in EN and DE from day one. A missing one renders as its " +
            "own key on a screen where two of the three options are already nothing but a sentence.");
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
    /// ⚠️ THREE OF THE FOUR ABSENCES THIS USED TO GUARD ARE GONE, because the run-tiles pass of
    /// 2026-08-20 built what they described: the campfire's perk upgrade and reroll grant, and the
    /// shrine's choice and Cleanse. What is left is one absence about the RUN — a hero holding no
    /// upgradeable perk — beside the shrine's two new captions and its prompt. Sharing a sentence
    /// between any two of them still tells the player one of these is the other.
    /// </remarks>
    [Fact]
    public void The_sentences_this_screen_adds_read_as_different_sentences()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        string?[] sentences =
        [
            snapshot.ReadText(EnglishStrings + UpgradePerkNoneBlockKey),
            snapshot.ReadText(EnglishStrings + TakeActionKey),
            snapshot.ReadText(EnglishStrings + CleanseActionKey),
            snapshot.ReadText(EnglishStrings + ShrineChooseLabelKey),
        ];

        sentences.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            sentences.Length,
            "two of this screen's own sentences are AUTHORED the same. A refused perk upgrade comes " +
            "back on the wire as the same ILLEGAL_STATE four other things do, and the shrine's two " +
            "captions sit on adjacent rows, so the wording is the only thing telling any of them " +
            $"apart: [{string.Join(" | ", sentences)}]");
    }

    /// <summary>
    /// 🔒 The ten shrine buff names stay the tuning file's, and this is the case that says so.
    /// </summary>
    /// <remarks>
    /// The shrine arm draws its two rows' names through these keys, so the temptation is to list
    /// them in the campfire document as well — which would leave two documents claiming one key and
    /// the second free to drift out of step with the pool it is supposed to describe.
    /// </remarks>
    [Fact]
    public void The_shrine_buff_names_are_already_carried_without_this_document_naming_them()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + ShrineBuffNameKeyAlreadyNamedByTuning)
                .ShouldNotBeNullOrWhiteSpace(
                    "the shrine buff pool's displayName members are what reference these keys, and " +
                    "the shrine arm renders them. If this is red the pool has stopped naming them, " +
                    "and the campfire document is not the place to start.");
    }

    /// <summary>
    /// 🔒 The guard proved to bite. Every other case here passes hardest when the orphan rule has
    /// stopped running, so one case has to break the reference on purpose.
    /// </summary>
    [Fact]
    public void A_campfire_string_the_document_stops_naming_is_rejected_as_an_orphan()
    {
        var source = RepoData.SourceWithEdit(
            CampfireDocument,
            $"\"rest\": \"{RestActionKey}\"",
            $"\"rest\": \"{TitleNameKey}\"");

        ContentLoader.Load(source).Issues.ShouldContain(
            i => i.Code == ContentIssueCode.OrphanedReference &&
                 i.Location == EnglishStrings + RestActionKey,
            "with nothing naming it, the rest caption becomes a translation somebody pays for twice " +
            "— and, because every issue is fatal, a client that will not load. Rest is also the one " +
            "campfire option that actually does something, so its caption going unnamed leaves a " +
            "screen of three options none of which reads as usable.");
    }
}
