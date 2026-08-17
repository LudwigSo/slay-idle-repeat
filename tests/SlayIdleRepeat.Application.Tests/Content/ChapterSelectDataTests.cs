using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The Chapter Select screen's strings as content: the document that names them, the schema that
/// governs it, and the pairing row that has to exist alongside both.
/// </summary>
/// <remarks>
/// 🔒 A loc key that nothing in the content set names is an <c>OrphanedReference</c>, and every
/// issue is fatal — so a screen whose strings were referenced only from C# would fail the content
/// load and take the game with it. Chapter NAMES are deliberately not here: each chapter document
/// carries its own <c>displayName</c> key, so a chapter added later brings its name with it.
/// </remarks>
public sealed class ChapterSelectDataTests
{
    private const string ChapterSelectDocument = "content/chapter_select/chapter_select.json";

    private const string ChapterSelectSchema = "schema/chapter_select.schema.json";

    private const string ChapterSelectDirectory = "content/chapter_select/";

    private const string TitleKey = "loc.chapter_select.title.name";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    [Fact]
    public void The_chapter_select_document_pairs_with_the_chapter_select_schema()
    {
        var shipped = RepoData.Documents.Keys;

        ContentLayout.SchemaFor(ChapterSelectDocument).ShouldBe(
            ChapterSelectSchema,
            "an unpaired data file is one nobody validates. This is the pairing the loader resolves, " +
            "and it is why a malformed document fails the build rather than the screen.");
        shipped.ShouldContain(
            ChapterSelectDocument,
            "and the pairing is only real if the document is. SchemaFor is a pure transform over a " +
            "string — it answers just as confidently for a path nobody ever authored.");
        shipped.ShouldContain(
            ChapterSelectSchema,
            "likewise the schema half: deleting it leaves SchemaFor answering exactly as it does " +
            "today, and this is the assertion that notices.");
    }

    /// <summary>
    /// 🔒 The declared row, not the answer the stem rule happens to give.
    /// </summary>
    [Fact]
    public void The_chapter_select_directory_is_declared_in_the_content_type_pairing_table()
    {
        ContentLayout.ContentTypeSchemas.ShouldContain(
            new KeyValuePair<string, string>(ChapterSelectDirectory, ChapterSelectSchema),
            "content pairs by DIRECTORY, and the table is the declaration. Leaving this row out " +
            "leans on the stem rule agreeing by coincidence, which it does today and stops doing " +
            "the moment the screen needs a second document.");
    }

    /// <summary>The consequence of the row above, stated as the behaviour it buys.</summary>
    [Fact]
    public void A_second_document_in_the_chapter_select_directory_still_pairs_with_the_one_schema()
    {
        ContentLayout.SchemaFor("content/chapter_select/chapter_select_tiers.json").ShouldBe(
            ChapterSelectSchema,
            "a content directory holds many files of one TYPE, all governed by that type's single " +
            "schema. Under the bare stem rule this would demand a schema named after the new file " +
            "and fail as MissingSchema — a build break landing on whoever adds it, for a pairing " +
            "decision made here.");
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_the_chapter_select_document_present()
    {
        var result = ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical);

        result.Succeeded.ShouldBeTrue(
            "an unreferenced loc key is fatal. If this is red, the Chapter Select strings were added " +
            "to the locales without the document that names them.");
    }

    [Theory]
    [InlineData(TitleKey)]
    [InlineData("loc.chapter_select.tier_normal.name")]
    [InlineData("loc.chapter_select.tier_heroic.name")]
    [InlineData("loc.chapter_select.tier_mythic.name")]
    [InlineData("loc.chapter_select.requires_clear.block")]
    [InlineData("loc.chapter_select.requires_legend_level.block")]
    [InlineData("loc.chapter_select.loading.status")]
    [InlineData("loc.chapter_select.profile_missing.status")]
    [InlineData("loc.chapter_select.unavailable.status")]
    [InlineData("loc.chapter_select.starting.status")]
    [InlineData("loc.chapter_select.started.status")]
    [InlineData("loc.chapter_select.refused.status")]
    [InlineData("loc.chapter_select.confirm.action")]
    public void Every_chapter_select_string_the_screen_shows_is_carried_by_both_locales(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' is a string the Chapter Select screen renders, and X-04 requires every " +
            "user-facing string to be a key in EN and DE from day one. A missing one renders as its " +
            "own key on the screen every run starts from.");
        snapshot.ReadText(GermanStrings + key).ShouldNotBe(
            snapshot.ReadText(EnglishStrings + key),
            "and the German file has to carry its own value rather than the English one copied " +
            "across. Stated as 'different from EN' rather than as any particular wording: the DE " +
            "values are untranslated placeholders today.");
    }

    /// <summary>
    /// 🔒 The tiers the screen can name are exactly the tiers the gating ladder authors. Two lists
    /// in two files that must not drift: a rung with no name draws a blank picker entry, and a name
    /// with no rung offers a difficulty nothing gates.
    /// </summary>
    [Fact]
    public void The_chapter_select_screen_names_exactly_the_tiers_the_gating_ladder_authors()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        var rungs = AuthoredMembers(snapshot, "tuning/progression.json#/chapterGating");
        var named = AuthoredMembers(snapshot, ChapterSelectDocument + "#/tier");

        named.Select(n => n.ToUpperInvariant()).ShouldBe(
            rungs,
            "the ladder is the vocabulary and the picker is the view of it. A fourth tier authored " +
            "in tuning with no caption here is a row the player is offered blank; a caption here " +
            "with no rung is a difficulty the ladder cannot gate, and this screen is the only gate " +
            "there is.");
    }

    private static IReadOnlyList<string> AuthoredMembers(
        SlayIdleRepeat.Core.Content.ContentSnapshot snapshot, string reference) =>
        snapshot.Read(reference).MemberNames
                .Where(n => !n.StartsWith('_'))
                .Order(StringComparer.Ordinal)
                .ToArray();

    /// <summary>
    /// 🔒 The guard proved to bite. Every other case here passes hardest when the orphan rule has
    /// stopped running, so one case has to break the reference on purpose.
    /// </summary>
    [Fact]
    public void A_chapter_select_string_the_document_stops_naming_is_rejected_as_an_orphan()
    {
        var source = RepoData.SourceWithEdit(
            ChapterSelectDocument,
            $"\"title\": \"{TitleKey}\"",
            "\"title\": \"loc.chapter_select.confirm.action\"");

        ContentLoader.Load(source).Issues.ShouldContain(
            i => i.Code == ContentIssueCode.OrphanedReference &&
                 i.Location == EnglishStrings + TitleKey,
            "with nothing naming it, the screen's heading becomes a translation somebody pays for " +
            "twice — and, because every issue is fatal, a client that will not load. This is the " +
            "failure the document exists to prevent, so it has to be demonstrated.");
    }
}
