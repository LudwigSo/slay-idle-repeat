using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Parity;

/// <summary>
/// The three refusals the committed parity table's reader makes, exercised against tables the
/// committed one deliberately is not.
/// </summary>
/// <remarks>
/// The committed file passes all three, so nothing that only reads it can tell whether the refusals
/// exist. Each case here mutates a copy and asserts the specific refusal.
/// </remarks>
public sealed class ParityBaselineRefusalTests
{
    [Fact]
    public void A_regenerated_table_nobody_reviewed_is_refused()
    {
        var unreviewed = ParityBaseline.RawText.Replace(
            "\"status\": \"reviewed\"", "\"status\": \"unreviewed\"", StringComparison.Ordinal);

        unreviewed.ShouldNotBe(
            ParityBaseline.RawText,
            "the mutation has to land, or this case asserts the committed table's own refusal — " +
            "which does not happen.");

        Should.Throw<FormatException>(() => Read(unreviewed))
            .Message.ShouldContain("unreviewed", Case.Sensitive);
    }

    [Fact]
    public void A_review_with_no_reason_is_refused()
    {
        Should.Throw<FormatException>(() => Read(WithReviewField("why", string.Empty)))
            .Message.ShouldContain(
                "review.why",
                Case.Sensitive,
                "'reviewed' with no reason is a checkbox rather than a record of what moved.");
    }

    /// <summary>
    /// A review naming neither a task nor a date is refused — one case, because the loader states it
    /// as one rule.
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>Validate</c> checks <c>why</c>, <c>reviewedBy</c> and <c>reviewedOn</c> in a single
    /// condition and answers a single message that names all three, so no assertion over that message
    /// can say WHICH field was blank. Splitting this into two cases would read as though it could.
    /// What is genuinely asserted is that each field is load-bearing: blanking either one on its own
    /// is refused.
    /// </remarks>
    [Theory]
    [InlineData("reviewedBy")]
    [InlineData("reviewedOn")]
    public void A_review_that_names_neither_a_task_nor_a_date_is_refused(string field)
    {
        Should.Throw<FormatException>(() => Read(WithReviewField(field, string.Empty)))
            .Message.ShouldContain(
                "does not say who concluded what",
                Case.Sensitive,
                $"blanking '{field}' alone must still be refused: a 'reviewed' stamp with no task or " +
                "no date behind it is a checkbox rather than a record.");
    }

    [Fact]
    public void A_named_row_that_cannot_say_what_it_pins_is_refused()
    {
        var pinned = ParityBaseline.Named[0];

        // Concatenated rather than re-serialised: the writer renders with the relaxed encoder, so a
        // non-ASCII character is raw UTF-8 in the file and JsonSerializer's default escape would
        // search for text the file does not contain — and the mutation would silently not land.
        var blanked = ParityBaseline.RawText.Replace(
            $"\"why\": \"{pinned.Why}\"", "\"why\": \"\"", StringComparison.Ordinal);

        blanked.ShouldNotBe(
            ParityBaseline.RawText, "the mutation has to land for this case to mean anything.");

        Should.Throw<FormatException>(() => Read(blanked))
            .Message.ShouldContain(pinned.Id, Case.Sensitive);
    }

    /// <summary>The negative control: the committed table passes every refusal above.</summary>
    [Fact]
    public void The_committed_table_itself_passes()
    {
        using var document = JsonDocument.Parse(ParityBaseline.RawText);

        Should.NotThrow(() => ParityBaseline.Validate(document));
    }

    private static void Read(string json)
    {
        using var document = JsonDocument.Parse(json);

        ParityBaseline.Validate(document);
    }

    /// <summary>The committed table with one review field replaced.</summary>
    private static string WithReviewField(string field, string value)
    {
        using var document = JsonDocument.Parse(ParityBaseline.RawText);
        var current = document.RootElement.GetProperty("review").GetProperty(field).GetString()!;

        var replaced = ParityBaseline.RawText.Replace(
            $"\"{field}\": \"{current}\"", $"\"{field}\": \"{value}\"", StringComparison.Ordinal);

        replaced.ShouldNotBe(
            ParityBaseline.RawText,
            $"the '{field}' mutation has to land, or the case asserts nothing.");

        return replaced;
    }
}
