using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// The inbox templates as content: the document that names them, the schema that governs it, and the
/// pairing row that has to exist alongside both.
/// </summary>
/// <remarks>
/// 🔒 A loc key that nothing in the content set names is an <c>OrphanedReference</c>, and every issue
/// is fatal — so templates referenced only from server code would fail the content load and take the
/// game with them. <c>content/mail/mail.json</c> is what names them, and these cases are what stop it
/// being quietly deleted or emptied.
/// </remarks>
public sealed class MailDataTests
{
    private const string MailDocument = "content/mail/mail.json";

    private const string MailSchema = "schema/mail.schema.json";

    private const string MailDirectory = "content/mail/";

    private const string OutageBodyKey = "loc.mail.compensation.outage.body";

    private const string EnglishStrings = "loc/en.json#/strings/";

    private const string GermanStrings = "loc/de.json#/strings/";

    [Fact]
    public void The_mail_document_pairs_with_the_mail_schema()
    {
        var shipped = RepoData.Documents.Keys;

        ContentLayout.SchemaFor(MailDocument).ShouldBe(
            MailSchema,
            "an unpaired data file is one nobody validates, and this one decides what ops may send.");
        shipped.ShouldContain(
            MailDocument,
            "and the pairing is only real if the document is. SchemaFor is a pure transform over a " +
            "string — it answers just as confidently for a path nobody ever authored.");
        shipped.ShouldContain(MailSchema, "likewise the schema half.");
    }

    [Fact]
    public void The_mail_directory_is_declared_in_the_content_type_pairing_table()
    {
        ContentLayout.ContentTypeSchemas.ShouldContain(
            new KeyValuePair<string, string>(MailDirectory, MailSchema),
            "content pairs by DIRECTORY. Ops copy grows by document — one per incident class — and " +
            "without the row the day the second lands the stem rule demands a schema per file.");
    }

    [Fact]
    public void A_second_mail_document_still_pairs_with_the_one_mail_schema()
    {
        ContentLayout.SchemaFor("content/mail/mail_incidents.json").ShouldBe(MailSchema);
    }

    [Fact]
    public void The_shipped_data_set_still_validates_with_the_mail_document_present()
    {
        ContentLoader.Load(RepoData.Source(), ContentLoadOptions.Canonical).Succeeded.ShouldBeTrue(
            "an unreferenced loc key is fatal. If this is red, mail strings were added to the " +
            "locales without the document that names them.");
    }

    [Theory]
    [InlineData("loc.mail.announcement.maintenance.title")]
    [InlineData("loc.mail.announcement.maintenance.body")]
    [InlineData("loc.mail.announcement.issue_resolved.title")]
    [InlineData("loc.mail.announcement.issue_resolved.body")]
    [InlineData("loc.mail.compensation.outage.title")]
    [InlineData(OutageBodyKey)]
    [InlineData("loc.mail.reconciliation.event_currency.title")]
    [InlineData("loc.mail.reconciliation.event_currency.body")]
    [InlineData("loc.mail.moderation.report_outcome.title")]
    [InlineData("loc.mail.moderation.report_outcome.body")]
    [InlineData("loc.mail.account.new_device.title")]
    [InlineData("loc.mail.account.new_device.body")]
    public void Every_mail_string_is_carried_by_both_locales(string key)
    {
        var snapshot = ContentLoader.Load(RepoData.Source()).Require();

        snapshot.ReadText(EnglishStrings + key).ShouldNotBeNullOrWhiteSpace(
            $"'{key}' is a string a player reads, and every user-facing string is a key in EN and " +
            "DE from day one. A missing one renders as its own key inside a compensation notice.");
        snapshot.ReadText(GermanStrings + key).ShouldNotBe(
            snapshot.ReadText(EnglishStrings + key),
            "and the German file carries its own value rather than the English one copied across. " +
            "Stated as 'different from EN' rather than as any wording: the DE values are " +
            "untranslated placeholders, and pinning their text would assert a translation nobody did.");
    }

    /// <summary>
    /// 🔒 The guard proved to bite. Every other case here passes hardest when the orphan rule has
    /// stopped running, so one case has to break the reference on purpose.
    /// </summary>
    [Fact]
    public void A_mail_string_the_mail_document_stops_naming_is_rejected_as_an_orphan()
    {
        var source = RepoData.SourceWithEdit(
            MailDocument,
            $"\"body\": \"{OutageBodyKey}\"",
            "\"body\": \"loc.mail.announcement.issue_resolved.body\"");

        ContentLoader.Load(source).Issues.ShouldContain(
            i => i.Code == ContentIssueCode.OrphanedReference &&
                 i.Location == EnglishStrings + OutageBodyKey,
            "with nothing naming it, the compensation body becomes a translation somebody pays for " +
            "twice — and, because every issue is fatal, a client that will not load.");
    }

    /// <summary>
    /// 🔒 The second half of the same guard: the document may not name a string the locales do not
    /// carry. Without this, a template could be authored against copy that does not exist and the
    /// send-time check would be the only thing between it and a player.
    /// </summary>
    [Fact]
    public void A_mail_template_naming_a_string_no_locale_carries_is_rejected()
    {
        var source = RepoData.SourceWithEdit(
            MailDocument,
            $"\"body\": \"{OutageBodyKey}\"",
            "\"body\": \"loc.mail.compensation.never_written.body\"");

        // 🔴 The identity, not the symptom. The edit does TWO things at once — it points the
        // template at a string nothing carries AND it un-names the outage body — and the second
        // raises an issue unconditionally. A disjunction over two codes with no location was
        // therefore satisfied whether or not this case's own subject fired at all, and it was the
        // sibling orphan rule above that kept it green. Pinned to the DANGLING REFERENCE, in the
        // mail document, naming the string that does not exist.
        var issues = ContentLoader.Load(source).Issues;

        issues.ShouldContain(
            i => i.Location.StartsWith(MailDocument, StringComparison.Ordinal) &&
                 i.Message.Contains("loc.mail.compensation.never_written.body", StringComparison.Ordinal),
            "a template whose text does not exist is a message that renders as its own key. The " +
            "issue must be reported against the MAIL DOCUMENT and name the string it invented — " +
            "reporting only the string the edit orphaned is the sibling rule above, not this one." +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                issues.Select(i => i.Code + " @ " + i.Location + " — " + i.Message)));
    }
}
