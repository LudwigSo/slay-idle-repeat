using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The Dice Forge screen's strings as content: the document that names them, the schema that governs
/// it, and the pairing row that has to exist alongside both.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 A loc key that nothing in the content set names is an <c>OrphanedReference</c>, and every
/// issue is fatal — so a screen whose strings were referenced only from C# would fail the content
/// load and take the game with it. <c>content/dice_forge/dice_forge.json</c> is what names them, and
/// these cases are what stop it being quietly deleted or emptied.
/// </para>
/// <para>
/// 🔴 <b>`13` authors no layout section for this tile.</b> It is not one of the numbered screens, so
/// the document is shaped like its three siblings rather than to a spec — a heading, the faces of
/// the run's own die, the upgrades the rules layer offers, one commit control, the named reason two
/// upgrades are withheld, and one status line per state.
/// </para>
/// <para>
/// ⚠️ The member names below are the shape the document must be authored in, because the orphan case
/// anchors on one of them as literal text.
/// </para>
/// </remarks>
public sealed class DiceForgeDataTests
{
    private const string DiceForgeDocument = "content/dice_forge/dice_forge.json";

    private const string DiceForgeSchema = "schema/dice_forge.schema.json";

    private const string DiceForgeDirectory = "content/dice_forge/";

    private const string TitleNameKey = "loc.dice_forge.title.name";

    private const string UpgradeActionKey = "loc.dice_forge.upgrade.action";

    private const string WithheldBlockKey = "loc.dice_forge.withheld.block";

    private const string NotAtAForgeStatusKey = "loc.dice_forge.not_at_a_forge.status";

    private const string RefusedStatusKey = "loc.dice_forge.refused.status";

    private const string HostUnavailableStatusKey = "loc.dice_forge.host_unavailable.status";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    [Fact]
    public void The_dice_forge_document_pairs_with_the_dice_forge_schema()
    {
        var shipped = RepoData.Documents.Keys;

        ContentLayout.SchemaFor(DiceForgeDocument).ShouldBe(
            DiceForgeSchema,
            "an unpaired data file is one nobody validates. This is the pairing the loader resolves, " +
            "and it is the reason a malformed forge document fails the build rather than the screen.");
        shipped.ShouldContain(
            DiceForgeDocument,
            "and the pairing is only real if the document is. SchemaFor is a pure transform over a " +
            "string — it answers just as confidently for a path nobody ever authored, so the " +
            "assertion above passes unchanged against a checkout carrying neither file.");
        shipped.ShouldContain(
            DiceForgeSchema,
            "likewise the schema half: deleting it leaves SchemaFor answering exactly as it does " +
            "today, and this is the assertion that notices.");
    }

    /// <summary>
    /// The declared row, not the answer the stem rule happens to give — the same easy case every
    /// other screen's directory is in, and the same reason for declaring it anyway.
    /// </summary>
    [Fact]
    public void The_dice_forge_directory_is_declared_in_the_content_type_pairing_table()
    {
        ContentLayout.ContentTypeSchemas.ShouldContain(
            new KeyValuePair<string, string>(DiceForgeDirectory, DiceForgeSchema),
            "content pairs by DIRECTORY, and the table is the declaration. Leaving this row out " +
            "leans on the stem rule agreeing by coincidence, which it does today and stops doing " +
            "the moment a second document lands beside this one.");
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_the_dice_forge_document_present()
    {
        var result = ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical);

        result.Succeeded.ShouldBeTrue(
            "an unreferenced loc key is fatal. If this is red, the forge strings were added to the " +
            "locales without the document that names them — which is a client that cannot load its " +
            "own content.");
    }

    [Theory]
    [InlineData(TitleNameKey)]
    [InlineData("loc.dice_forge.face.label")]
    [InlineData(UpgradeActionKey)]
    [InlineData("loc.dice_forge.higher_pip.name")]
    [InlineData("loc.dice_forge.to_surge.name")]
    [InlineData("loc.dice_forge.to_fortune.name")]
    [InlineData(WithheldBlockKey)]
    [InlineData("loc.dice_forge.loading.status")]
    [InlineData("loc.dice_forge.run_missing.status")]
    [InlineData(NotAtAForgeStatusKey)]
    [InlineData("loc.dice_forge.read_unavailable.status")]
    [InlineData(RefusedStatusKey)]
    [InlineData(HostUnavailableStatusKey)]
    public void Every_dice_forge_string_the_screen_shows_is_carried_by_both_locales(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' is a string the Dice Forge screen renders, and X-04 requires every " +
            "user-facing string to be a key in EN and DE from day one. A missing one renders as its " +
            "own key on a screen whose whole content is captions.");
        snapshot.ReadText(GermanStrings + key).ShouldNotBe(
            snapshot.ReadText(EnglishStrings + key),
            "and the German file has to carry its own value rather than the English one copied " +
            "across. Stated as 'different from EN' rather than as any particular wording: the DE " +
            "values are untranslated placeholders today, and pinning their text would assert a " +
            "translation that has not been done.");
    }

    /// <summary>
    /// 🔒 The three upgrade names read as three different sentences, <b>as authored</b>.
    /// </summary>
    /// <remarks>
    /// They are the whole content of the option list: a player choosing between them has nothing
    /// else to go on, and two that read the same is a menu with two of one thing.
    /// </remarks>
    [Fact]
    public void The_three_offered_upgrades_read_as_three_different_sentences()
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        string?[] names =
        [
            snapshot.ReadText(EnglishStrings + "loc.dice_forge.higher_pip.name"),
            snapshot.ReadText(EnglishStrings + "loc.dice_forge.to_surge.name"),
            snapshot.ReadText(EnglishStrings + "loc.dice_forge.to_fortune.name"),
        ];

        names.Distinct(StringComparer.Ordinal).Count().ShouldBe(
            names.Length,
            $"two of the forge's three upgrades are AUTHORED the same: [{string.Join(" | ", names)}]");
    }

    /// <summary>
    /// 🔒 The guard proved to bite. Every other case here passes hardest when the orphan rule has
    /// stopped running, so one case has to break the reference on purpose.
    /// </summary>
    [Fact]
    public void A_dice_forge_string_the_document_stops_naming_is_rejected_as_an_orphan()
    {
        var source = RepoData.SourceWithEdit(
            DiceForgeDocument,
            $"\"upgrade\": \"{UpgradeActionKey}\"",
            $"\"upgrade\": \"{TitleNameKey}\"");

        ContentLoader.Load(source).Issues.ShouldContain(
            i => i.Code == ContentIssueCode.OrphanedReference &&
                 i.Location == EnglishStrings + UpgradeActionKey,
            "with nothing naming it, the forge caption becomes a translation somebody pays for twice " +
            "— and, because every issue is fatal, a client that will not load. Forging is also the " +
            "ONLY action this screen has, so its caption going unnamed is a run standing on a tile " +
            "it cannot read its way off.");
    }
}
