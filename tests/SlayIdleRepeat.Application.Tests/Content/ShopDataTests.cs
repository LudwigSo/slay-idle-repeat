using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The run Shop screen's strings as content: the document that names them, the schema that governs
/// it, and the pairing row that has to exist alongside both.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 A loc key that nothing in the content set names is an <c>OrphanedReference</c>, and every
/// issue is fatal — so a screen whose strings were referenced only from C# would fail the content
/// load and take the game with it. <c>content/shop/shop.json</c> is what names them, and these
/// cases are what stop it being quietly deleted or emptied.
/// </para>
/// <para>
/// 🔴 <b>The <c>block</c> group holds the one sentence this screen is mostly made of.</b> A run's
/// shop stocks nothing: the rules layer refuses every purchase and every refresh because the run
/// row carries no offer state at all, and the offer, refresh and consumable model is a later
/// milestone's. So the screen says why there is nothing to buy, in words, and shows neither a buy
/// slot nor a refresh control — an affordance for an offer that does not exist would assert one.
/// That sentence has to be different from the ones for a shop the player is not standing at, a
/// refused departure and a host that did not answer, because all four are "the shop did nothing"
/// and only the wording tells them apart.
/// </para>
/// <para>
/// ⚠️ The member names below are the shape the document must be authored in, because the orphan
/// case anchors on one of them as literal text. Two-space indentation and one space after the
/// colon, exactly as <c>content/board/board.json</c> is authored.
/// </para>
/// <para>
/// ⚠️ These keys are the RUN shop's, three segments deep. The four-segment <c>loc.shop.staple.*</c>
/// and <c>loc.shop.daily.*</c> keys already in the locales belong to the meta shop's offer
/// catalogue and are a different screen's; the two sets share a domain segment and nothing else.
/// </para>
/// </remarks>
public sealed class ShopDataTests
{
    private const string ShopDocument = "content/shop/shop.json";

    private const string ShopSchema = "schema/shop.schema.json";

    private const string ShopDirectory = "content/shop/";

    private const string LeaveActionKey = "loc.shop.leave.action";

    private const string TitleNameKey = "loc.shop.title.name";

    private const string OfferUnavailableStatusKey = "loc.shop.offer_unavailable.status";

    private const string UnaffordableStatusKey = "loc.shop.unaffordable.status";

    private const string NotAtAShopStatusKey = "loc.shop.not_at_a_shop.status";

    private const string RefusedStatusKey = "loc.shop.refused.status";

    private const string HostUnavailableStatusKey = "loc.shop.host_unavailable.status";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    [Fact]
    public void The_shop_document_pairs_with_the_shop_schema()
    {
        var shipped = RepoData.Documents.Keys;

        ContentLayout.SchemaFor(ShopDocument).ShouldBe(
            ShopSchema,
            "an unpaired data file is one nobody validates. This is the pairing the loader resolves, " +
            "and it is the reason a malformed shop document fails the build rather than the screen.");
        shipped.ShouldContain(
            ShopDocument,
            "and the pairing is only real if the document is. SchemaFor is a pure transform over a " +
            "string — it answers just as confidently for a path nobody ever authored, so the " +
            "assertion above passes unchanged against a checkout carrying neither file.");
        shipped.ShouldContain(
            ShopSchema,
            "likewise the schema half: deleting schema/shop.schema.json leaves SchemaFor answering " +
            "exactly as it does today, and this is the assertion that notices.");
    }

    /// <summary>
    /// 🔒 The declared row, not the answer the stem rule happens to give — the same easy case the
    /// pairing table's own remarks name: it answers correctly while there is exactly one file, and
    /// the gap only becomes visible as a wrongly-resolved path the day a second arrives.
    /// </summary>
    /// <remarks>
    /// 🔴 And this directory is the one most likely to see a second file: the offer catalogue this
    /// screen cannot yet stock is a later milestone's, and it lands here.
    /// </remarks>
    [Fact]
    public void The_shop_directory_is_declared_in_the_content_type_pairing_table()
    {
        ContentLayout.ContentTypeSchemas.ShouldContain(
            new KeyValuePair<string, string>(ShopDirectory, ShopSchema),
            "content pairs by DIRECTORY, and the table is the declaration. Leaving this row out " +
            "leans on the stem rule agreeing by coincidence, which it does today and stops doing " +
            "the moment the shop's offer catalogue lands beside this document.");
    }

    /// <summary>The consequence of the row above, stated as the behaviour it buys.</summary>
    [Fact]
    public void A_second_document_in_the_shop_directory_still_pairs_with_the_one_schema()
    {
        ContentLayout.SchemaFor("content/shop/shop_offers.json").ShouldBe(
            ShopSchema,
            "a content directory holds many files of one TYPE, all governed by that type's single " +
            "schema. Under the bare stem rule this would demand schema/shop_offers.schema.json and " +
            "fail as MissingSchema — a build break landing on whoever adds the file, for a pairing " +
            "decision made here.");
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_the_shop_document_present()
    {
        var result = ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical);

        result.Succeeded.ShouldBeTrue(
            "an unreferenced loc key is fatal. If this is red, the shop strings were added to the " +
            "locales without the document that names them — which is a client that cannot load its " +
            "own content.");
    }

    [Theory]
    [InlineData(TitleNameKey)]
    [InlineData(LeaveActionKey)]
    [InlineData("loc.shop.buy.action")]
    [InlineData("loc.shop.refresh.action")]
    [InlineData("loc.shop.gold.label")]
    [InlineData("loc.shop.slot.perk.name")]
    [InlineData("loc.shop.slot.consumable.name")]
    [InlineData("loc.shop.slot.run_buff.name")]
    [InlineData("loc.shop.slot.heal.name")]
    [InlineData("loc.shop.sold.label")]
    [InlineData("loc.shop.unaffordable.label")]
    [InlineData("loc.shop.empty_slot.label")]
    [InlineData("loc.shop.refresh_spent.block")]
    [InlineData(OfferUnavailableStatusKey)]
    [InlineData(UnaffordableStatusKey)]
    [InlineData("loc.shop.loading.status")]
    [InlineData("loc.shop.run_missing.status")]
    [InlineData(NotAtAShopStatusKey)]
    [InlineData("loc.shop.read_unavailable.status")]
    [InlineData(RefusedStatusKey)]
    [InlineData(HostUnavailableStatusKey)]
    public void Every_shop_string_the_screen_shows_is_carried_by_both_locales(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' is a string the run Shop screen renders, and X-04 requires every user-facing " +
            "string to be a key in EN and DE from day one. A missing one renders as its own key on " +
            "a screen whose entire content is words.");
        snapshot.ReadText(GermanStrings + key).ShouldNotBe(
            snapshot.ReadText(EnglishStrings + key),
            "and the German file has to carry its own value rather than the English one copied " +
            "across. Stated as 'different from EN' rather than as any particular wording: the DE " +
            "values are untranslated placeholders today, and pinning their text would assert a " +
            "translation that has not been done.");
    }

    /// <summary>
    /// 🔒 The four ways this screen can come to nothing read as four different sentences, <b>as
    /// authored</b>.
    /// </summary>
    /// <remarks>
    /// 🔴 A screen opened where no shop is, a shop whose contents could not be read, a refusal, a
    /// purchase the run cannot afford and a host that never answered all look identical: a page of
    /// text and no purchase. Two that read the same send the player — and whoever reads their bug
    /// report — after the wrong one of five problems, one of which is not a problem at all.
    /// ⚠️ Five rather than four: the shop-has-nothing-in-stock sentence is gone, because the shop
    /// stocks; the offer-unreadable and cannot-afford sentences are the two that replaced it.
    /// </remarks>
    [Fact]
    public void The_ways_the_shop_comes_to_nothing_read_as_different_sentences()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        string?[] sentences =
        [
            snapshot.ReadText(EnglishStrings + NotAtAShopStatusKey),
            snapshot.ReadText(EnglishStrings + OfferUnavailableStatusKey),
            snapshot.ReadText(EnglishStrings + RefusedStatusKey),
            snapshot.ReadText(EnglishStrings + UnaffordableStatusKey),
            snapshot.ReadText(EnglishStrings + HostUnavailableStatusKey),
        ];

        sentences.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            sentences.Length,
            "two of the shop's sentences are AUTHORED the same, so the screen has stopped " +
            "distinguishing a missing shop from an unreadable offer, from a refusal, from a purchase " +
            $"the run cannot afford, from a host that did not answer: [{string.Join(" | ", sentences)}]");
    }

    /// <summary>
    /// 🔒 The guard proved to bite. Every other case here passes hardest when the orphan rule has
    /// stopped running, so one case has to break the reference on purpose.
    /// </summary>
    [Fact]
    public void A_shop_string_the_document_stops_naming_is_rejected_as_an_orphan()
    {
        var source = RepoData.SourceWithEdit(
            ShopDocument,
            $"\"leave\": \"{LeaveActionKey}\"",
            $"\"leave\": \"{TitleNameKey}\"");

        ContentLoader.Load(source).Issues.ShouldContain(
            i => i.Code == ContentIssueCode.OrphanedReference &&
                 i.Location == EnglishStrings + LeaveActionKey,
            "with nothing naming it, the leave caption becomes a translation somebody pays for twice " +
            "— and, because every issue is fatal, a client that will not load. Leaving is also the " +
            "ONLY action this screen has, so its caption going unnamed is a run standing on a tile " +
            "it cannot read its way off.");
    }
}
