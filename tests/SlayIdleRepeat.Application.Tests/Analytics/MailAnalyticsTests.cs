using Shouldly;
using SlayIdleRepeat.Application.Services.Analytics;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Analytics;

/// <summary>The two inbox names the sink emits, and the boundary of what the claim stream carries.</summary>
public sealed class MailAnalyticsTests
{
    private static DispatchedEvents Batch(params DomainEvent[] events)
    {
        var (game, player) = Worlds.InARun();

        return new DispatchedEvents(player, new ClaimInboxCommand(), game.State(player), events);
    }

    [Fact]
    public void A_claimed_message_is_emitted_as_mail_claimed()
    {
        var emitted = AnalyticsTranslator.Translate(
                Batch(new MailClaimed(1, new MessageId("MSG_a"), MessageCategory.COMPENSATION)))
            .Where(e => e.Name == AnalyticsVocabulary.MailClaimed)
            .ShouldHaveSingleItem("one claimed message is exactly one mail_claimed.");

        emitted.Properties["message_id"].ShouldBe("MSG_a", "which message paid out.");
        emitted.Properties["category"].ShouldBe(
            "COMPENSATION",
            "and which kind it was, because 'how much compensation did we actually deliver' is the " +
            "question this event exists to answer after an incident.");
    }

    [Fact]
    public void A_claim_of_several_messages_emits_one_event_each()
    {
        AnalyticsTranslator.Translate(Batch(
                new MailClaimed(1, new MessageId("MSG_a"), MessageCategory.COMPENSATION),
                new MailClaimed(2, new MessageId("MSG_b"), MessageCategory.RECONCILIATION)))
            .Count(e => e.Name == AnalyticsVocabulary.MailClaimed)
            .ShouldBe(
                2,
                "CLAIM ALL is one command and many messages; an event per command would report a " +
                "player who collected forty rewards as having collected one.");
    }

    [Fact]
    public void The_category_is_read_off_the_event_rather_than_assumed()
    {
        // The discriminating counterpart: a translator that hard-coded COMPENSATION passes the case
        // above and fails here.
        AnalyticsTranslator.Translate(
                Batch(new MailClaimed(1, new MessageId("MSG_a"), MessageCategory.ACCOUNT)))
            .Single(e => e.Name == AnalyticsVocabulary.MailClaimed)
            .Properties["category"].ShouldBe("ACCOUNT");
    }

    [Fact]
    public void A_claim_that_paid_nothing_emits_nothing()
    {
        AnalyticsTranslator.Translate(Batch()).ShouldBeEmpty(
            "an accepted CLAIM_INBOX over an empty inbox is a real command that reported no fact. " +
            "A name emitted for the command rather than for the grant would report a claim every " +
            "time a client polled.");
    }

    [Fact]
    public void Both_inbox_names_are_in_the_emitted_vocabulary()
    {
        AnalyticsVocabulary.Emitted.ShouldContain(AnalyticsVocabulary.MailClaimed);
        AnalyticsVocabulary.Emitted.ShouldContain(
            AnalyticsVocabulary.MailExpiredAutogranted,
            "the nightly sweep tracks through the port directly — a hosted job is not a command — so " +
            "nothing in the translator would otherwise put this name in the closed set the " +
            "architecture rules read.");
    }

    [Fact]
    public void The_inbox_names_are_the_ones_the_design_set_authors()
    {
        AnalyticsVocabulary.MailClaimed.ShouldBe("mail_claimed");
        AnalyticsVocabulary.MailExpiredAutogranted.ShouldBe(
            "mail_expired_autogranted",
            "spelled as the design set spells it: a dashboard is built against the name, and a " +
            "renamed event is a metric that silently starts at zero.");
    }
}
