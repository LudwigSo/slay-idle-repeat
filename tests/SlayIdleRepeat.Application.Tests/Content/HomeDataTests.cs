using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The Home screen's strings as content: the document that names them, the schema that governs it,
/// and the pairing row that has to exist alongside both.
/// </summary>
/// <remarks>
/// 🔒 A loc key that nothing in the content set names is an <c>OrphanedReference</c>, and every
/// issue is fatal — so a screen whose strings were referenced only from C# would fail the content
/// load and take the game with it. <c>content/home/home.json</c> is what names them, and these cases
/// are what stop it being quietly deleted or emptied.
/// </remarks>
public sealed class HomeDataTests
{
    private const string HomeDocument = "content/home/home.json";

    private const string HomeSchema = "schema/home.schema.json";

    private const string HomeDirectory = "content/home/";

    private const string LegendLevelLabelKey = "loc.home.legend_level.label";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    [Fact]
    public void The_home_document_pairs_with_the_home_schema()
    {
        var shipped = RepoData.Documents.Keys;

        ContentLayout.SchemaFor(HomeDocument).ShouldBe(
            HomeSchema,
            "an unpaired data file is one nobody validates. This is the pairing the loader resolves, " +
            "and it is the reason a malformed home document fails the build rather than the screen.");
        shipped.ShouldContain(
            HomeDocument,
            "and the pairing is only real if the document is. SchemaFor is a pure transform over a " +
            "string — it answers just as confidently for a path nobody ever authored, so the " +
            "assertion above passes unchanged against a checkout carrying neither file.");
        shipped.ShouldContain(
            HomeSchema,
            "likewise the schema half: deleting schema/home.schema.json leaves SchemaFor answering " +
            "exactly as it does today, and this is the assertion that notices.");
    }

    /// <summary>
    /// 🔒 The declared row, not the answer the stem rule happens to give — the same easy case the
    /// pairing table's own remarks name: it answers correctly while there is exactly one file, and
    /// the gap only becomes visible as a wrongly-resolved path the day a second arrives.
    /// </summary>
    [Fact]
    public void The_home_directory_is_declared_in_the_content_type_pairing_table()
    {
        ContentLayout.ContentTypeSchemas.ShouldContain(
            new KeyValuePair<string, string>(HomeDirectory, HomeSchema),
            "content pairs by DIRECTORY, and the table is the declaration. Leaving this row out " +
            "leans on the stem rule agreeing by coincidence, which it does today and stops doing " +
            "the moment the Home screen needs a second document.");
    }

    /// <summary>The consequence of the row above, stated as the behaviour it buys.</summary>
    [Fact]
    public void A_second_document_in_the_home_directory_still_pairs_with_the_one_home_schema()
    {
        ContentLayout.SchemaFor("content/home/home_widgets.json").ShouldBe(
            HomeSchema,
            "a content directory holds many files of one TYPE, all governed by that type's single " +
            "schema. Under the bare stem rule this would demand schema/home_widgets.schema.json and " +
            "fail as MissingSchema — a build break landing on whoever adds the file, for a pairing " +
            "decision made here.");
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_the_home_document_present()
    {
        var result = ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical);

        result.Succeeded.ShouldBeTrue(
            "an unreferenced loc key is fatal. If this is red, the Home strings were added to the " +
            "locales without the document that names them — which is a client that cannot load its " +
            "own content.");
    }

    [Theory]
    [InlineData(LegendLevelLabelKey)]
    [InlineData("loc.home.energy.label")]
    [InlineData("loc.home.energy_reserve.label")]
    [InlineData("loc.home.start_run.action")]
    [InlineData("loc.home.continue_run.action")]
    [InlineData("loc.home.loading.status")]
    [InlineData("loc.home.unavailable.status")]
    [InlineData("loc.home.power.label")]
    [InlineData("loc.home.progress.label")]
    [InlineData("loc.home.stage.label")]
    [InlineData("loc.home.nothing_cleared.status")]
    public void Every_home_string_the_screen_shows_is_carried_by_both_locales(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' is a string the Home screen renders, and X-04 requires every user-facing " +
            "string to be a key in EN and DE from day one. A missing one renders as its own key on " +
            "the screen a player sees every session.");
        snapshot.ReadText(GermanStrings + key).ShouldNotBe(
            snapshot.ReadText(EnglishStrings + key),
            "and the German file has to carry its own value rather than the English one copied " +
            "across. Stated as 'different from EN' rather than as any particular wording: the DE " +
            "values are untranslated placeholders today, and pinning their text would assert a " +
            "translation that has not been done.");
    }

    /// <summary>
    /// 🔒 The captions the HUD borrows from elsewhere, named under the home document's own
    /// <c>label</c> member. The orphan rule cannot catch these: each key is already named by the
    /// document that owns it, so a home document that dropped one still loads — and the presenter
    /// reading <c>label/crowns</c> would find nothing.
    /// </summary>
    [Theory]
    [InlineData("crowns", "loc.currency.crowns.name")]
    [InlineData("soulShards", "loc.currency.soul_shards.name")]
    [InlineData("gold", "loc.currency.gold.name")]
    [InlineData("tierNormal", "loc.chapter_select.tier_normal.name")]
    [InlineData("tierHeroic", "loc.chapter_select.tier_heroic.name")]
    [InlineData("tierMythic", "loc.chapter_select.tier_mythic.name")]
    public void The_home_document_names_each_caption_the_hud_borrows_from_another_screen(string member, string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText($"{HomeDocument}#/label/{member}").ShouldBe(
            key,
            $"the HUD's '{member}' tile is captioned with a string another document already owns, " +
            "and the home document has to say WHICH one: a caption authored twice under two keys is " +
            "translated twice and drifts on the first edit to either.");
    }

    /// <summary>
    /// 🔒 The guard proved to bite. Every other case here passes hardest when the orphan rule has
    /// stopped running, so one case has to break the reference on purpose.
    /// </summary>
    [Fact]
    public void A_home_string_the_home_document_stops_naming_is_rejected_as_an_orphan()
    {
        var source = RepoData.SourceWithEdit(
            HomeDocument,
            $"\"legendLevel\": \"{LegendLevelLabelKey}\"",
            "\"legendLevel\": \"loc.home.energy.label\"");

        ContentLoader.Load(source).Issues.ShouldContain(
            i => i.Code == ContentIssueCode.OrphanedReference &&
                 i.Location == EnglishStrings + LegendLevelLabelKey,
            "with nothing naming it, the Legend Level caption becomes a translation somebody pays for " +
            "twice — and, because every issue is fatal, a client that will not load. This is the " +
            "failure the home document exists to prevent, so it has to be demonstrated.");
    }
}
