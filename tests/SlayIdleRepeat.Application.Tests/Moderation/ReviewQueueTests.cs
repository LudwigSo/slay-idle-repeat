using Shouldly;
using SlayIdleRepeat.Application.Moderation;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Moderation;

/// <summary>
/// The review queue's own rules: a producer may only raise an open entry, a reviewer's verdict is
/// final, and every verdict carries the operator string nothing in this system authenticates.
/// </summary>
public sealed class ReviewQueueTests
{
    private static readonly PlayerId Subject = new("PLAYER_subject");
    private static readonly DateTimeOffset Raised = new(2026, 8, 28, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reviewed = Raised.AddHours(6);

    private static ReviewQueueEntry Open() =>
        ReviewQueueEntry.Raise(
            "REV_1", ReviewSource.PLAUSIBILITY_SWEEP, Subject, "currency 900000 over 1.00:00:00", Raised);

    [Fact]
    public void A_raised_entry_is_open_and_carries_no_verdict()
    {
        var entry = Open();

        entry.State.ShouldBe(
            ReviewState.OPEN,
            "a producer may only ever raise an open entry — a queue that can be created already "
            + "decided is a queue that can decide automatically.");
        entry.ReviewedBy.ShouldBeNull();
        entry.ReviewedAtUtc.ShouldBeNull();
        entry.ReviewNotes.ShouldBeNull();
        entry.Subject.ShouldBe(Subject);
        entry.Source.ShouldBe(ReviewSource.PLAUSIBILITY_SWEEP);
    }

    [Fact]
    public void Confirming_records_the_unverified_operator_string_the_verdict_and_the_instant()
    {
        var confirmed = Open().Confirm("ops.rita", "reproduced against the economy log", Reviewed);

        confirmed.State.ShouldBe(ReviewState.CONFIRMED);
        confirmed.ReviewedBy.ShouldBe(
            "ops.rita",
            "🔒 there is no operator identity system anywhere in this repository — player auth is "
            + "the only auth that exists. This string is recorded, never verified, and every reader "
            + "has to know that.");
        confirmed.ReviewNotes.ShouldBe("reproduced against the economy log");
        confirmed.ReviewedAtUtc.ShouldBe(Reviewed);
        confirmed.EntryId.ShouldBe("REV_1", "a verdict never changes which entry it is about.");
    }

    [Fact]
    public void Dismissing_records_the_same_three_things_and_a_different_state()
    {
        var dismissed = Open().Dismiss("ops.rita", "legitimate — the account bought a bundle", Reviewed);

        dismissed.State.ShouldBe(ReviewState.DISMISSED);
        dismissed.ReviewedBy.ShouldBe("ops.rita");
        dismissed.ReviewNotes.ShouldBe("legitimate — the account bought a bundle");
        dismissed.ReviewedAtUtc.ShouldBe(Reviewed);
    }

    [Fact]
    public void A_confirmed_entry_cannot_be_dismissed_and_a_dismissed_one_cannot_be_confirmed()
    {
        var confirmed = Open().Confirm("ops.rita", "confirmed", Reviewed);
        var dismissed = Open().Dismiss("ops.rita", "dismissed", Reviewed);

        Should.Throw<InvalidOperationException>(
                () => confirmed.Dismiss("ops.sam", "second opinion", Reviewed.AddHours(1)))
            .Message.ShouldContain(
                "CONFIRMED",
                Case.Sensitive,
                "the refusal has to say which verdict already stands, or a reviewer cannot tell "
                + "whose decision they just collided with.");

        Should.Throw<InvalidOperationException>(
                () => dismissed.Confirm("ops.sam", "second opinion", Reviewed.AddHours(1)))
            .Message.ShouldContain("DISMISSED", Case.Sensitive);
    }

    [Fact]
    public void A_decided_entry_cannot_be_decided_the_same_way_twice()
    {
        var confirmed = Open().Confirm("ops.rita", "confirmed", Reviewed);
        var dismissed = Open().Dismiss("ops.rita", "dismissed", Reviewed);

        Should.Throw<InvalidOperationException>(
            () => confirmed.Confirm("ops.rita", "confirmed again", Reviewed.AddHours(1)));
        Should.Throw<InvalidOperationException>(
            () => dismissed.Dismiss("ops.rita", "dismissed again", Reviewed.AddHours(1)));
    }

    [Fact]
    public void A_verdict_leaves_the_open_entry_untouched_and_carries_everything_else_across()
    {
        var open = Open();
        var confirmed = open.Confirm("ops.rita", "notes", Reviewed);

        open.State.ShouldBe(ReviewState.OPEN, "the record is immutable; a verdict returns a new one.");
        open.ReviewedBy.ShouldBeNull();

        confirmed.EntryId.ShouldBe(open.EntryId);
        confirmed.Source.ShouldBe(open.Source);
        confirmed.Subject.ShouldBe(open.Subject);
        confirmed.Reason.ShouldBe(
            open.Reason,
            "a verdict decides an entry; it never rewrites what the producer observed.");
        confirmed.RaisedAtUtc.ShouldBe(open.RaisedAtUtc);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_verdict_with_no_operator_is_refused_and_the_refusal_blames_that_parameter(string? operatorName)
    {
        Should.Throw<ArgumentException>(() => Open().Confirm(operatorName!, "notes", Reviewed))
            .ParamName.ShouldBe(
                "reviewedBy",
                "two independent rules on this method throw the same type; without the parameter "
                + "name an implementation that validated the notes twice would pass.");
        Should.Throw<ArgumentException>(() => Open().Dismiss(operatorName!, "notes", Reviewed))
            .ParamName.ShouldBe("reviewedBy");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void A_verdict_with_no_note_is_refused_and_the_refusal_blames_that_parameter(string? notes)
    {
        Should.Throw<ArgumentException>(() => Open().Confirm("ops.rita", notes!, Reviewed))
            .ParamName.ShouldBe("notes");
        Should.Throw<ArgumentException>(() => Open().Dismiss("ops.rita", notes!, Reviewed))
            .ParamName.ShouldBe("notes");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_entry_raised_with_no_reason_is_refused_and_the_refusal_blames_that_parameter(string? reason)
    {
        Should.Throw<ArgumentException>(
                () => ReviewQueueEntry.Raise("REV_1", ReviewSource.PLAUSIBILITY_SWEEP, Subject, reason!, Raised))
            .ParamName.ShouldBe("reason");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_entry_raised_with_no_id_is_refused_and_the_refusal_blames_that_parameter(string? entryId)
    {
        Should.Throw<ArgumentException>(
                () => ReviewQueueEntry.Raise(entryId!, ReviewSource.PLAUSIBILITY_SWEEP, Subject, "reason", Raised))
            .ParamName.ShouldBe("entryId");
    }

    /// <summary>
    /// 🔒 The shared-queue promise: three producers, one queue. A value dropped from this enum is a
    /// producer that would need its own queue, which is exactly what the design forbids.
    /// </summary>
    [Fact]
    public void The_queue_names_every_producer_that_will_feed_it()
    {
        Enum.GetValues<ReviewSource>().ShouldBe(
            new[] { ReviewSource.PLAUSIBILITY_SWEEP, ReviewSource.DUEL_ANTI_CHEAT, ReviewSource.PLAYER_REPORT },
            ignoreOrder: true,
            "the duel anti-cheat pass and player reports route into this same queue, reviewed by "
            + "the same people through the same ladder.");
    }
}
