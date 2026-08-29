using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The connection-state strings as content: the document that names them, the schema that governs
/// it, and the pairing row that has to exist alongside both.
/// </summary>
/// <remarks>
/// 🔒 A loc key that nothing in the content set names is an <c>OrphanedReference</c>, and every issue
/// is fatal — so connection copy referenced only from C# would fail the content load and take the
/// game with it. <c>content/net/net.json</c> is what names these four, and these cases are what stop
/// it being quietly deleted or emptied.
/// </remarks>
public sealed class NetDataTests
{
    private const string NetDocument = "content/net/net.json";

    private const string NetSchema = "schema/net.schema.json";

    private const string NetDirectory = "content/net/";

    private const string ReconnectingKey = "loc.net.reconnecting.status";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    [Fact]
    public void The_net_document_pairs_with_the_net_schema()
    {
        var shipped = RepoData.Documents.Keys;

        ContentLayout.SchemaFor(NetDocument).ShouldBe(
            NetSchema,
            "an unpaired data file is one nobody validates. This is the pairing the loader resolves, " +
            "and it is the reason a malformed net document fails the build rather than the boot.");
        shipped.ShouldContain(
            NetDocument,
            "and the pairing is only real if the document is. SchemaFor is a pure transform over a " +
            "string — it answers just as confidently for a path nobody ever authored.");
        shipped.ShouldContain(
            NetSchema,
            "likewise the schema half: deleting schema/net.schema.json leaves SchemaFor answering " +
            "exactly as it does today, and this is the assertion that notices.");
    }

    /// <summary>
    /// 🔒 The declared row, not the answer the stem rule happens to give — the same easy case
    /// <see cref="ContentLayout.ContentTypeSchemas"/>'s own remarks name, and the same one
    /// <c>BootDataTests</c> pins for <c>boot/</c>.
    /// </summary>
    [Fact]
    public void The_net_directory_is_declared_in_the_content_type_pairing_table()
    {
        ContentLayout.ContentTypeSchemas.ShouldContain(
            new KeyValuePair<string, string>(NetDirectory, NetSchema),
            "content pairs by DIRECTORY, and the table is the declaration. Leaving this row out " +
            "leans on the stem rule agreeing by coincidence, which it does today and stops doing " +
            "the moment the connection overlay needs a second document.");
    }

    /// <summary>The consequence of the row above, stated as the behaviour it buys.</summary>
    [Fact]
    public void A_second_document_in_the_net_directory_still_pairs_with_the_one_net_schema()
    {
        ContentLayout.SchemaFor("content/net/net_offline.json").ShouldBe(
            NetSchema,
            "a content directory holds many files of one TYPE, all governed by that type's single " +
            "schema. Under the bare stem rule this would demand schema/net_offline.schema.json and " +
            "fail as MissingSchema — a build break landing on whoever adds the file, for a pairing " +
            "decision made here.");
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_the_net_document_present()
    {
        var result = ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical);

        result.Succeeded.ShouldBeTrue(
            "the connection strings are the first loc keys in this repository written for something " +
            "drawn OVER a screen rather than for one, and an unreferenced key is fatal. If this is " +
            "red, the keys were added to the locales without the document that names them.");
    }

    [Theory]
    [InlineData(ReconnectingKey)]
    [InlineData("loc.net.waiting_for_connection.status")]
    [InlineData("loc.net.caught_up.status")]
    [InlineData("loc.net.run_resumed.status")]
    public void Every_connection_string_the_overlay_shows_is_carried_by_both_locales(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' is a string the connection overlay renders, and X-04 requires every " +
            "user-facing string to be a key in EN and DE from day one. A missing one renders as its " +
            "own key over whatever screen the player was on.");
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
    public void A_connection_string_the_net_document_stops_naming_is_rejected_as_an_orphan()
    {
        var source = RepoData.SourceWithEdit(
            NetDocument,
            $"\"reconnecting\": \"{ReconnectingKey}\"",
            "\"reconnecting\": \"loc.net.caught_up.status\"");

        ContentLoader.Load(source).Issues.ShouldContain(
            i => i.Code == ContentIssueCode.OrphanedReference &&
                 i.Location == EnglishStrings + ReconnectingKey,
            "with nothing naming it, the reconnecting string becomes a translation somebody pays for " +
            "twice — and, because every issue is fatal, a client that will not load. This is the " +
            "failure the net document exists to prevent, so it has to be demonstrated rather than " +
            "assumed.");
    }
}
