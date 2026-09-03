using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The Perk Draft screen's strings as content: the document that names them, the schema that
/// governs it, and the pairing row that has to exist alongside both.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 A loc key that nothing in the content set names is an <c>OrphanedReference</c>, and every
/// issue is fatal — so a screen whose strings were referenced only from C# would fail the content
/// load and take the game with it. <c>content/perk_draft/perk_draft.json</c> is what names them,
/// and these cases are what stop it being quietly deleted or emptied.
/// </para>
/// <para>
/// 🔴 The screen once carried sentences for three features that are not built — an ad reroll, a
/// fourth ad card, a free-reroll allowance. Those strings are retired rather than kept as
/// placeholders, and the negative theory below is what stops them coming back through the locales.
/// </para>
/// <para>
/// ⚠️ The member names below are the shape the document must be authored in, because the orphan
/// case anchors on one of them as literal text. Two-space indentation and one space after the
/// colon, exactly as <c>content/board/board.json</c> and <c>content/battle/battle.json</c> are
/// authored.
/// </para>
/// </remarks>
public sealed class PerkDraftDataTests
{
    private const string PerkDraftDocument = "content/perk_draft/perk_draft.json";

    private const string PerkDraftSchema = "schema/perk_draft.schema.json";

    private const string PerkDraftDirectory = "content/perk_draft/";

    private const string SkipActionKey = "loc.perk_draft.skip.action";

    private const string TitleNameKey = "loc.perk_draft.title.name";

    private const string RerollUnaffordableStatusKey = "loc.perk_draft.reroll_unaffordable.status";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    [Fact]
    public void The_perk_draft_document_pairs_with_the_perk_draft_schema()
    {
        var shipped = RepoData.Documents.Keys;

        ContentLayout.SchemaFor(PerkDraftDocument).ShouldBe(
            PerkDraftSchema,
            "an unpaired data file is one nobody validates. This is the pairing the loader resolves, " +
            "and it is the reason a malformed perk draft document fails the build rather than the screen.");
        shipped.ShouldContain(
            PerkDraftDocument,
            "and the pairing is only real if the document is. SchemaFor is a pure transform over a " +
            "string — it answers just as confidently for a path nobody ever authored, so the " +
            "assertion above passes unchanged against a checkout carrying neither file.");
        shipped.ShouldContain(
            PerkDraftSchema,
            "likewise the schema half: deleting schema/perk_draft.schema.json leaves SchemaFor " +
            "answering exactly as it does today, and this is the assertion that notices.");
    }

    /// <summary>
    /// 🔒 The declared row, not the answer the stem rule happens to give — the same easy case the
    /// pairing table's own remarks name: it answers correctly while there is exactly one file, and
    /// the gap only becomes visible as a wrongly-resolved path the day a second arrives.
    /// </summary>
    [Fact]
    public void The_perk_draft_directory_is_declared_in_the_content_type_pairing_table()
    {
        ContentLayout.ContentTypeSchemas.ShouldContain(
            new KeyValuePair<string, string>(PerkDraftDirectory, PerkDraftSchema),
            "content pairs by DIRECTORY, and the table is the declaration. Leaving this row out " +
            "leans on the stem rule agreeing by coincidence, which it does today and stops doing " +
            "the moment the draft screen needs a second document.");
    }

    /// <summary>The consequence of the row above, stated as the behaviour it buys.</summary>
    [Fact]
    public void A_second_document_in_the_perk_draft_directory_still_pairs_with_the_one_schema()
    {
        ContentLayout.SchemaFor("content/perk_draft/perk_draft_cards.json").ShouldBe(
            PerkDraftSchema,
            "a content directory holds many files of one TYPE, all governed by that type's single " +
            "schema. Under the bare stem rule this would demand schema/perk_draft_cards.schema.json " +
            "and fail as MissingSchema — a build break landing on whoever adds the file, for a " +
            "pairing decision made here.");
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_the_perk_draft_document_present()
    {
        var result = ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical);

        result.Succeeded.ShouldBeTrue(
            "an unreferenced loc key is fatal. If this is red, the perk draft strings were added to " +
            "the locales without the document that names them — which is a client that cannot load " +
            "its own content.");
    }

    [Theory]
    [InlineData(TitleNameKey)]
    [InlineData("loc.perk_draft.synergy.label")]
    [InlineData("loc.perk_draft.reroll_cost.label")]
    [InlineData("loc.perk_draft.skip_reward.label")]
    [InlineData("loc.perk_draft.upgrade.badge")]
    [InlineData("loc.perk_draft.reroll.action")]
    [InlineData(SkipActionKey)]
    [InlineData("loc.perk_draft.loading.status")]
    [InlineData("loc.perk_draft.no_draft.status")]
    [InlineData("loc.perk_draft.run_missing.status")]
    [InlineData("loc.perk_draft.read_unavailable.status")]
    [InlineData("loc.perk_draft.cards_unavailable.status")]
    [InlineData("loc.perk_draft.effect_numbers_unavailable.status")]
    [InlineData("loc.perk_draft.refused.status")]
    [InlineData(RerollUnaffordableStatusKey)]
    [InlineData("loc.perk_draft.host_unavailable.status")]
    public void Every_perk_draft_string_the_screen_shows_is_carried_by_both_locales(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' is a string the Perk Draft screen renders, and X-04 requires every " +
            "user-facing string to be a key in EN and DE from day one. A missing one renders as its " +
            "own key on the one screen a run's whole build is chosen on.");
        snapshot.ReadText(GermanStrings + key).ShouldNotBe(
            snapshot.ReadText(EnglishStrings + key),
            "and the German file has to carry its own value rather than the English one copied " +
            "across. Stated as 'different from EN' rather than as any particular wording: the DE " +
            "values are untranslated placeholders today, and pinning their text would assert a " +
            "translation that has not been done.");
    }

    /// <summary>
    /// 🔒 The strings for the ad reroll, the fourth ad card and the free-reroll allowance are gone
    /// from both locales — none of the three is built, and a sentence about one is a placeholder.
    /// </summary>
    [Theory]
    [InlineData("loc.perk_draft.ad_fourth_option.name")]
    [InlineData("loc.perk_draft.ad_reroll.action")]
    [InlineData("loc.perk_draft.ad_reroll_deferred.block")]
    [InlineData("loc.perk_draft.ad_fourth_option_deferred.block")]
    [InlineData("loc.perk_draft.free_reroll_unbuilt.block")]
    public void A_retired_placeholder_string_is_carried_by_neither_locale(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.TryRead(EnglishStrings + key, out _).ShouldBeFalse(
            $"'{key}' is still in the English locale. A key that survives there while the document " +
            "that named it is gone is an orphan the loader rejects, and one that survives in both " +
            "locales is a placeholder sentence the Perk Draft screen was told to stop drawing.");
        snapshot.TryRead(GermanStrings + key, out _).ShouldBeFalse(
            $"'{key}' is still in the German locale, so a translator is being paid for a sentence " +
            "about a feature the game does not have.");
    }

    /// <summary>
    /// 🔒 The guard proved to bite. Every other case here passes hardest when the orphan rule has
    /// stopped running, so one case has to break the reference on purpose.
    /// </summary>
    [Fact]
    public void A_perk_draft_string_the_document_stops_naming_is_rejected_as_an_orphan()
    {
        var source = RepoData.SourceWithEdit(
            PerkDraftDocument,
            $"\"skip\": \"{SkipActionKey}\"",
            $"\"skip\": \"{TitleNameKey}\"");

        ContentLoader.Load(source).Issues.ShouldContain(
            i => i.Code == ContentIssueCode.OrphanedReference &&
                 i.Location == EnglishStrings + SkipActionKey,
            "with nothing naming it, the skip caption becomes a translation somebody pays for twice " +
            "— and, because every issue is fatal, a client that will not load. The skip is also the " +
            "only way off this screen that costs the player nothing, so its caption going unnamed is " +
            "the failure this document most exists to prevent.");
    }
}
