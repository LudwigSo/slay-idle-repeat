using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Effects.Determinism;

/// <summary>
/// 🔒 The three refusals that make M2-17's baseline un-regenerable-into-green, driven on purpose.
/// </summary>
/// <remarks>
/// <para>
/// The whole design rests on one property: <b>a regenerated table does not pass.</b>
/// <c>DslDeterminismBaselineWriter</c> stamps <c>"unreviewed"</c> into every render and
/// <c>DslDeterminismBaseline.Validate</c> throws on it, so a determinism break cannot be made green
/// by re-running the writer — a human has to read the diff and write down why the hashes moved.
/// </para>
/// <para>
/// ⚠️ <b>Until this file existed, that property had no test.</b> The refusals ran inside the reader's
/// static initialiser, so the only document that ever reached them was the committed one, which
/// passes; and the two tests that looked like coverage —
/// <c>The_committed_table_was_reviewed_by_a_human</c> and
/// <c>Every_committed_row_says_what_it_pins</c> — were asserting conditions the initialiser had
/// already guaranteed. Both were removed. Steering S1: a test that cannot fail is a defect, not a
/// weak test.
/// </para>
/// <para>
/// 🔒 Each case asserts <b>which</b> refusal fired (steering S2), by a fragment of its own message —
/// the three messages are deliberately different, and a single <c>Should.Throw&lt;FormatException&gt;</c>
/// would not tell them apart. The pattern is <c>TunableMarkerAuditTests</c>', which drives an
/// <c>unreviewed</c> tunable-marker baseline in and matches on the refusal's wording.
/// </para>
/// <para>
/// Every case is the <b>committed</b> table with one region edited, rather than a hand-built
/// fixture: a fixture would drift from the real file's shape, and then these tests would pass while
/// the shipped document took a different path through the reader.
/// </para>
/// </remarks>
public sealed class DslDeterminismBaselineRefusalTests
{
    /// <summary>
    /// 🔒 The refusal <c>ContentValidator --write-baseline</c>'s reader performs, on this table.
    /// </summary>
    [Fact]
    public void A_table_still_stamped_unreviewed_is_refused()
    {
        var regenerated = Mutate(
            "\"status\": \"" + DslDeterminismBaselineWriter.ReviewedStatus + "\"",
            "\"status\": \"" + DslDeterminismBaselineWriter.UnreviewedStatus + "\"");

        var refusal = Should.Throw<FormatException>(() => Validate(regenerated));

        refusal.Message.ShouldContain(
            "is still 'unreviewed'",
            Case.Sensitive,
            "this is the status refusal, not one of the two 'why' refusals");
        refusal.Message.ShouldContain(
            "A generated reason is not a reason",
            Case.Sensitive,
            "the refusal restates the rule it is enforcing, so the reader of the failure does not " +
            "have to go and find it");
    }

    /// <summary>A table marked reviewed by a reviewer who wrote nothing down is refused.</summary>
    /// <remarks>
    /// ⚠️ The <c>why</c> case is a replace-all, so it blanks the review's reason <em>and</em> all
    /// twelve rows'. The review refusal is checked before the row refusal, so it is still the review
    /// refusal that fires and the assertion below still discriminates — but the edit is broader than
    /// the theory's name suggests, and <see cref="A_named_row_with_no_reason_is_refused"/> is the
    /// case that isolates the row half.
    /// </remarks>
    [Theory]
    [InlineData("why")]
    [InlineData("reviewedOn")]
    [InlineData("reviewedBy")]
    public void A_table_marked_reviewed_with_an_empty_review_field_is_refused(string field)
    {
        var blanked = Regex.Replace(
            DslDeterminismBaseline.RawText, $"\"{field}\": \"[^\"]*\"", $"\"{field}\": \"\"");

        blanked.ShouldNotBe(DslDeterminismBaseline.RawText, $"the '{field}' field has to have been edited");

        var refusal = Should.Throw<FormatException>(() => Validate(blanked));

        refusal.Message.ShouldContain(
            "does not say who concluded what",
            Case.Sensitive,
            "this is the review-block refusal, not the status refusal and not the named-row one");
    }

    /// <summary>A named row that cannot say what it pins is refused.</summary>
    [Fact]
    public void A_named_row_with_no_reason_is_refused()
    {
        // Only the LAST "why" in the file — the review block's is first, and blanking that would
        // fire the review refusal instead and prove nothing about the rows.
        var text = DslDeterminismBaseline.RawText;
        var last = text.LastIndexOf("\"why\": \"", StringComparison.Ordinal);
        last.ShouldBeGreaterThan(0);

        var end = text.IndexOf('"', last + "\"why\": \"".Length);
        var stripped = text[..(last + "\"why\": \"".Length)] + text[end..];

        var refusal = Should.Throw<FormatException>(() => Validate(stripped));

        refusal.Message.ShouldContain(
            "carry no 'why'",
            Case.Sensitive,
            "this is the named-row refusal, not the review-block one");
        refusal.Message.ShouldContain(
            "condition-gated-an-effect-out",
            Case.Sensitive,
            "the refusal names the offending rows, so the fix does not start with a search");
    }

    // 🔴 There was a `The_committed_table_as_committed_is_accepted` positive control here, and it
    //    could not fail: reaching DslDeterminismBaseline.RawText runs the static initialiser, which
    //    is Validate(JsonDocument.Parse(RawText)), so by the time the test body ran the very same
    //    document had already validated in the same process. It was removed rather than reworded.
    //    The control it was meant to provide is inside each case above instead: every one asserts
    //    that its edit actually changed the text (Mutate's ShouldContain, the theory's
    //    ShouldNotBe, the row case's ShouldBeGreaterThan), so a refusal that fired for some
    //    unrelated reason would show up as a harness failure rather than as a pass.

    private static string Mutate(string from, string to)
    {
        var text = DslDeterminismBaseline.RawText;
        text.ShouldContain(from, Case.Sensitive, "the committed table has to contain what is being replaced");

        return text.Replace(from, to, StringComparison.Ordinal);
    }

    private static void Validate(string json)
    {
        using var document = JsonDocument.Parse(json);
        DslDeterminismBaseline.Validate(document);
    }
}
