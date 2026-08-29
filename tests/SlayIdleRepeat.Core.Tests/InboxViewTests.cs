using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Handlers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>The read-only inbox projection: its order, its identity rule, and what it calls claimable.</summary>
public sealed class InboxViewTests
{
    [Fact]
    public void Messages_are_ordered_oldest_first()
    {
        var view = InboxWorlds.View(
            InboxWorlds.Message("MSG_new", createdAtUtc: Worlds.NowUtc.AddDays(-1)),
            InboxWorlds.Message("MSG_old", createdAtUtc: Worlds.NowUtc.AddDays(-9)));

        view.Messages.Select(m => m.Id.Value).ShouldBe(
            new[] { "MSG_old", "MSG_new" },
            "the order the claim pays in, and an Energy overflow depends on it.");
    }

    [Fact]
    public void Messages_sent_in_the_same_instant_are_ordered_by_id()
    {
        var sent = Worlds.NowUtc.AddDays(-1);
        var view = InboxWorlds.View(
            InboxWorlds.Message("MSG_b", createdAtUtc: sent),
            InboxWorlds.Message("MSG_a", createdAtUtc: sent));

        view.Messages.Select(m => m.Id.Value).ShouldBe(
            new[] { "MSG_a", "MSG_b" },
            "a segment send writes every copy at one instant, so without the second key the claim " +
            "order of a whole cohort's mail would be whatever the store happened to answer with.");
    }

    [Fact]
    public void Two_messages_under_one_id_are_refused()
    {
        Should.Throw<ArgumentException>(() => InboxWorlds.View(
                InboxWorlds.Message("MSG_same"), InboxWorlds.Message("MSG_same")))
            .Message.ShouldContain(
                "idempotent ON THAT ID",
                Case.Sensitive,
                "a grant is idempotent on the message id, so two rows under one id are two rows the " +
                "claim path treats as one — paying one and stamping both, or owing a reward twice.");
    }

    [Fact]
    public void An_empty_inbox_is_not_the_same_value_as_an_unloaded_one()
    {
        InboxView.Empty.Messages.ShouldBeEmpty();
        InboxView.Empty.Claimable.ShouldBeEmpty();
    }

    [Fact]
    public void A_message_holding_nothing_is_not_claimable()
    {
        var announcement = InboxWorlds.Message(
            "MSG_news", MessageCategory.ANNOUNCEMENT, attachments: Array.Empty<MailAttachment>());

        announcement.IsClaimable.ShouldBeFalse(
            "an announcement is read, not collected. Stamping it claimed would spend the one field " +
            "that says 'these rewards were paid'.");
        announcement.FirstRefusal.ShouldBeNull(
            "and it is not REFUSED either — there is simply nothing to pay, which is a different " +
            "answer from 'this build cannot pay it'.");
    }

    [Fact]
    public void A_message_is_held_whole_by_one_attachment_this_build_cannot_pay()
    {
        var mixed = InboxWorlds.Message("MSG_mixed", attachments: new[]
        {
            new MailAttachment("SOUL_SHARDS", 500),
            new MailAttachment("CHEST", 1),
        });

        mixed.IsClaimable.ShouldBeFalse(
            "all of the attachments or none: a message that paid its currencies and held back its " +
            "chest would be stamped claimed with a reward still owed and nothing holding the debt.");
        mixed.FirstRefusal.ShouldBe(
            MailAttachmentRefusal.CONTAINER_SHELF_ABSENT,
            "and it says WHICH absence held it, so 'not built yet' never reads as 'nothing to claim'.");
    }

    [Fact]
    public void A_claimed_message_is_no_longer_claimable()
    {
        InboxWorlds.Message("MSG_spent", claimedAtUtc: Worlds.NowUtc).IsClaimable.ShouldBeFalse();
    }

    [Fact]
    public void Two_views_over_equal_messages_are_equal()
    {
        InboxWorlds.View(InboxWorlds.Message("MSG_a")).ShouldBe(
            InboxWorlds.View(InboxWorlds.Message("MSG_a")),
            "equality is hand-written over the sequences: the synthesized version compares the " +
            "lists by REFERENCE, so a view read out of the same rows twice would compare unequal.");
    }

    [Fact]
    public void Two_views_over_different_attachments_are_not_equal()
    {
        InboxWorlds.View(InboxWorlds.Message("MSG_a")).ShouldNotBe(
            InboxWorlds.View(InboxWorlds.Message(
                "MSG_a", attachments: new[] { new MailAttachment("CROWNS", 1) })),
            "the negative control: an equality that compared only ids would satisfy the case above " +
            "while calling two different rewards the same message.");
    }

    [Fact]
    public void Finding_a_message_the_inbox_does_not_hold_answers_null()
    {
        InboxWorlds.View(InboxWorlds.Message("MSG_a")).Find(new MessageId("MSG_b")).ShouldBeNull();
    }

    [Fact]
    public void The_view_renders_its_size_rather_than_every_message()
    {
        InboxWorlds.View(InboxWorlds.Message("MSG_a")).ToString().ShouldBe(
            "InboxView { Messages = 1, Claimable = 1 }",
            "a slice's ToString() lands in diagnostics; forty rendered messages there is a log line " +
            "nobody reads.");
    }
}
