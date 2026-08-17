using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The boot screen's strings as content: the document that names them, the schema that governs it,
/// and the pairing row that has to exist alongside both.
/// </summary>
/// <remarks>
/// 🔒 A loc key that nothing in the content set names is an <c>OrphanedReference</c>, and every
/// issue is fatal — so a boot screen whose strings were referenced only from C# would fail the
/// content load and take the game with it. <c>content/boot/boot.json</c> is what names them, and
/// these cases are what stop it being quietly deleted or emptied.
/// </remarks>
public sealed class BootDataTests
{
    private const string BootDocument = "content/boot/boot.json";

    private const string BootSchema = "schema/boot.schema.json";

    private const string BootDirectory = "content/boot/";

    private const string TitleKey = "loc.boot.title.name";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    [Fact]
    public void The_boot_document_pairs_with_the_boot_schema()
    {
        ContentLayout.SchemaFor(BootDocument).ShouldBe(
            BootSchema,
            "an unpaired data file is one nobody validates. This is the pairing the loader resolves, " +
            "and it is the reason a malformed boot document fails the build rather than the boot.");
    }

    /// <summary>
    /// 🔒 The declared row, not the answer the stem rule happens to give. <c>boot.json</c> sits in a
    /// directory named after itself, which <see cref="ContentLayout.ContentTypeSchemas"/>'s own
    /// remarks call the easy case to forget: the stem rule answers correctly while there is exactly
    /// one file, and the gap only becomes visible as a wrongly-resolved path the day a second
    /// arrives.
    /// </summary>
    [Fact]
    public void The_boot_directory_is_declared_in_the_content_type_pairing_table()
    {
        ContentLayout.ContentTypeSchemas.ShouldContain(
            new KeyValuePair<string, string>(BootDirectory, BootSchema),
            "content pairs by DIRECTORY, and the table is the declaration. Leaving this row out " +
            "leans on the stem rule agreeing by coincidence, which it does today and stops doing " +
            "the moment the boot screen needs a second document.");
    }

    /// <summary>The consequence of the row above, stated as the behaviour it buys.</summary>
    [Fact]
    public void A_second_document_in_the_boot_directory_still_pairs_with_the_one_boot_schema()
    {
        ContentLayout.SchemaFor("content/boot/boot_failure.json").ShouldBe(
            BootSchema,
            "a content directory holds many files of one TYPE, all governed by that type's single " +
            "schema. Under the bare stem rule this would demand schema/boot_failure.schema.json and " +
            "fail as MissingSchema — a build break landing on whoever adds the file, for a pairing " +
            "decision made here.");
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_the_boot_document_present()
    {
        var result = ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical);

        result.Succeeded.ShouldBeTrue(
            "the boot strings are the first loc keys in this repository written for a screen rather " +
            "than for a catalogue entry, and an unreferenced one is fatal. If this is red, the keys " +
            "were added to the locales without the document that names them — which is a client that " +
            "cannot load its own content.");
    }

    [Theory]
    [InlineData(TitleKey)]
    [InlineData("loc.boot.splash.status")]
    [InlineData("loc.boot.content.status")]
    [InlineData("loc.boot.profile.status")]
    [InlineData("loc.boot.atlas.status")]
    [InlineData("loc.boot.ready.status")]
    [InlineData("loc.boot.failure.status")]
    public void Every_boot_string_the_screen_shows_is_carried_by_both_locales(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' is a string the boot screen renders, and X-04 requires every user-facing " +
            "string to be a key in EN and DE from day one. A missing one renders as its own key on " +
            "the first screen a player ever sees.");
        snapshot.ReadText(GermanStrings + key).ShouldNotBe(
            snapshot.ReadText(EnglishStrings + key),
            "and the German file has to carry its own value rather than the English one copied " +
            "across. Stated as 'different from EN' rather than as any particular wording: the DE " +
            "values are untranslated placeholders today, and pinning their text would assert a " +
            "translation that has not been done.");
    }

    /// <summary>
    /// 🔒 The guard proved to bite. Every other case here passes hardest when the orphan rule has
    /// stopped running, so one case has to break the reference on purpose.
    /// </summary>
    [Fact]
    public void A_boot_string_the_boot_document_stops_naming_is_rejected_as_an_orphan()
    {
        var source = RepoData.SourceWithEdit(
            BootDocument, $"\"title\": \"{TitleKey}\"", "\"title\": \"loc.boot.ready.status\"");

        ContentLoader.Load(source).Issues.ShouldContain(
            i => i.Code == ContentIssueCode.OrphanedReference &&
                 i.Location == EnglishStrings + TitleKey,
            "with nothing naming it, the title string becomes a translation somebody pays for twice " +
            "— and, because every issue is fatal, a client that will not load. This is the failure " +
            "the boot document exists to prevent, so it has to be demonstrated rather than assumed.");
    }
}
