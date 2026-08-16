using Shouldly;
using SlayIdleRepeat.Application.Services.Content;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Content;

/// <summary>
/// <c>luck.schema.json</c> against the shipped <c>luck.json</c>, and specifically against the blocks
/// M4-01b wires: the keyless <c>sustainAntiBrick</c> and the chest pick's guarantee.
/// </summary>
/// <remarks>
/// <c>RealDataSetTests</c> already asserts that the whole shipped set loads with no issues, which
/// covers this document as one of twenty. These cases are the other direction: they prove the schema
/// would <em>reject</em> the specific mistakes an author of the new block could make. A schema that
/// declared the block and validated nothing about it would pass the whole-set case forever.
/// </remarks>
public sealed class LuckSchemaTests
{
    private const string Document = "tuning/luck.json";

    private static IReadOnlyList<ContentIssue> Issues(string find, string replaceWith) =>
        ContentLoader.Load(RepoData.SourceWithEdit(Document, find, replaceWith)).Issues;

    /// <summary>The shipped document, with its new block, still validates.</summary>
    /// <remarks>
    /// The control. Every case below asserts a rejection, and all of them would pass against a schema
    /// that rejected the document outright.
    /// </remarks>
    [Fact]
    public void The_shipped_document_validates()
    {
        ContentLoader.Load(RepoData.Source()).Issues.ShouldBeEmpty();
    }

    /// <summary>The anti-brick block is <b>required</b>, not merely permitted.</summary>
    /// <remarks>
    /// 🔒 The mutation <b>deletes</b> the block rather than misspelling its name, and the difference
    /// is the whole case. A misspelled member is caught by <c>additionalProperties: false</c> — the
    /// rule asserted two cases below — so a probe that renamed it would go red against a schema that
    /// never listed the block in <c>required</c> at all. A block the schema knows about but does not
    /// require is one a retune can delete, and the reader then throws at load for every player at
    /// once rather than failing the build.
    /// </remarks>
    [Fact]
    public void The_draft_block_requires_the_anti_brick()
    {
        Issues(AuthoredAntiBrickBlock, string.Empty)
            .ShouldContain(issue => issue.Code == ContentIssueCode.SchemaViolation);
    }

    /// <summary>
    /// The whole authored block, sliced out of the shipped document rather than transcribed.
    /// </summary>
    /// <remarks>
    /// Read from the file so the anchor cannot go stale on a reworded <c>_doc</c>, a reflowed line or
    /// a line-ending convention — three ways a transcribed literal stops occurring, at which point
    /// the mutation helper refuses and the case reads as broken rather than as passing.
    /// </remarks>
    private static string AuthoredAntiBrickBlock
    {
        get
        {
            var document = RepoData.Documents[Document];
            var start = document.IndexOf("\"sustainAntiBrick\"", StringComparison.Ordinal);
            var end = document.IndexOf("\"qualityFloor\"", start, StringComparison.Ordinal);

            return document[start..end];
        }
    }

    /// <summary>A force category outside the perk vocabulary is rejected at build time.</summary>
    [Fact]
    public void An_unknown_force_category_is_rejected()
    {
        Issues("\"forceCategory\": \"SUSTAIN\"", "\"forceCategory\": \"HEALING\"")
            .ShouldContain(issue => issue.Code == ContentIssueCode.UnknownId);
    }

    /// <summary>And so is the enum's PascalCase spelling — the vocabulary is the document's, not C#'s.</summary>
    /// <remarks>
    /// The second probe. A schema whose enum listed both spellings would pass the case above while
    /// letting two tokens name one category, and the reader parses case-sensitively.
    /// </remarks>
    [Fact]
    public void The_PascalCase_spelling_of_a_category_is_rejected()
    {
        Issues("\"forceCategory\": \"SUSTAIN\"", "\"forceCategory\": \"Sustain\"")
            .ShouldContain(issue => issue.Code == ContentIssueCode.UnknownId);
    }

    /// <summary>A member the anti-brick block does not declare is rejected.</summary>
    /// <remarks>
    /// The block is closed on purpose: `24` §4.7 authors no number for this rule, so a
    /// <c>stageIndex</c> appearing here would be a dial invented by whoever added it.
    /// </remarks>
    [Fact]
    public void An_undeclared_member_of_the_anti_brick_block_is_rejected()
    {
        Issues(
            "\"forceCategory\": \"SUSTAIN\"",
            "\"forceCategory\": \"SUSTAIN\", \"afterStage\": 2")
            .ShouldContain(issue => issue.Code == ContentIssueCode.UnknownId);
    }

    /// <summary>A chest-pick guarantee of zero picks is rejected.</summary>
    /// <remarks>
    /// The negative control on the whole file: a schema that validated nothing would pass every case
    /// above by rejecting nothing, and this is a rule the block already had before M4-01b touched it.
    /// </remarks>
    [Fact]
    public void A_chest_pick_guarantee_of_zero_is_rejected()
    {
        Issues(
            "\"guaranteeAfterConsecutiveMisses\": 4",
            "\"guaranteeAfterConsecutiveMisses\": 0")
            .ShouldContain(issue => issue.Code == ContentIssueCode.OutOfRange);
    }
}
