using System.Text.Json;
using Shouldly;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Determinism;

/// <summary>
/// The three refusals the committed <c>LogHash</c> table's reader makes, exercised against tables the
/// committed one deliberately is not.
/// </summary>
/// <remarks>
/// The committed file passes all three, so nothing that only reads it can tell whether the refusals
/// exist. Each case here mutates a copy and asserts the specific refusal, which is the only way this
/// guard can be shown to bite.
/// </remarks>
public sealed class BattleLogHashBaselineRefusalTests
{
    [Fact]
    public void A_regenerated_table_nobody_reviewed_is_refused()
    {
        var unreviewed = BattleLogHashBaseline.RawText.Replace(
            "\"status\": \"reviewed\"", "\"status\": \"unreviewed\"", StringComparison.Ordinal);

        unreviewed.ShouldNotBe(
            BattleLogHashBaseline.RawText,
            "the mutation has to land, or this case asserts the committed table's own refusal — " +
            "which does not happen.");

        Should.Throw<FormatException>(() => Read(unreviewed))
            .Message.ShouldContain(
                "unreviewed",
                Case.Sensitive,
                "the regeneration command stamps 'unreviewed' into every render, so this refusal is " +
                "what stops a determinism break being made green by re-running the writer.");
    }

    [Fact]
    public void A_review_with_no_reason_is_refused()
    {
        var reasonless = WithReviewField("why", string.Empty);

        Should.Throw<FormatException>(() => Read(reasonless))
            .Message.ShouldContain(
                "review.why",
                Case.Sensitive,
                "'reviewed' with no reason is a checkbox. The reason names the change that moved the " +
                "hashes and the task that made it.");
    }

    [Fact]
    public void A_review_that_names_nobody_is_refused()
    {
        Should.Throw<FormatException>(() => Read(WithReviewField("reviewedBy", string.Empty)))
            .Message.ShouldContain("review.reviewedBy", Case.Sensitive);

        Should.Throw<FormatException>(() => Read(WithReviewField("reviewedOn", string.Empty)))
            .Message.ShouldContain("review.reviewedOn", Case.Sensitive);
    }

    [Fact]
    public void A_named_row_that_cannot_say_what_it_pins_is_refused()
    {
        var pinned = BattleLogHashBaseline.Named[0];

        // Concatenated rather than re-serialised: the writer renders with the relaxed encoder, so an
        // em dash is a raw UTF-8 character in the file and JsonSerializer's default — escape
        // would search for text the file does not contain — and the mutation would silently not land.
        var blanked = BattleLogHashBaseline.RawText.Replace(
            $"\"why\": \"{pinned.Why}\"", "\"why\": \"\"", StringComparison.Ordinal);

        blanked.ShouldNotBe(
            BattleLogHashBaseline.RawText, "the mutation has to land for this case to mean anything.");

        Should.Throw<FormatException>(() => Read(blanked))
            .Message.ShouldContain(
                pinned.Id,
                Case.Sensitive,
                "the refusal names the row, because 'a row is missing a why' with twelve rows in the " +
                "file is not something a reader can act on.");
    }

    /// <summary>The negative control: the committed table passes every refusal above.</summary>
    [Fact]
    public void The_committed_table_itself_passes()
    {
        using var document = JsonDocument.Parse(BattleLogHashBaseline.RawText);

        Should.NotThrow(() => BattleLogHashBaseline.Validate(document));
    }

    private static void Read(string json)
    {
        using var document = JsonDocument.Parse(json);

        BattleLogHashBaseline.Validate(document);
    }

    /// <summary>The committed table with one review field replaced.</summary>
    private static string WithReviewField(string field, string value)
    {
        using var document = JsonDocument.Parse(BattleLogHashBaseline.RawText);
        var current = document.RootElement.GetProperty("review").GetProperty(field).GetString()!;

        var replaced = BattleLogHashBaseline.RawText.Replace(
            $"\"{field}\": \"{current}\"", $"\"{field}\": \"{value}\"", StringComparison.Ordinal);

        replaced.ShouldNotBe(
            BattleLogHashBaseline.RawText,
            $"the '{field}' mutation has to land, or the case asserts nothing.");

        return replaced;
    }
}
