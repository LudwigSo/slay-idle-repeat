using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Services.Inbox;

/// <summary>Retention and capacity: what expires, what a full inbox may drop, and what it may not.</summary>
public sealed class InboxRetentionTests
{
    [Fact]
    public void An_ordinary_message_expires_after_the_retention_window()
    {
        InboxRetention.ExpiryFor(MessageCategory.COMPENSATION, Worlds.Start).ShouldBe(
            Worlds.Start.AddDays(30),
            "thirty days, and the reward is auto-granted rather than destroyed when it goes.");
    }

    [Theory]
    [InlineData(MessageCategory.MODERATION)]
    [InlineData(MessageCategory.ACCOUNT)]
    public void A_record_message_never_expires(MessageCategory category)
    {
        InboxRetention.ExpiryFor(category, Worlds.Start).ShouldBeNull(
            "a null expiry is what makes it the record; a date thirty days out would have the " +
            "nightly job delete a sanction notice.");
    }

    [Fact]
    public void An_inbox_inside_its_capacity_drops_nothing()
    {
        var stored = Enumerable.Range(0, InboxRetention.Capacity)
            .Select(i => InboxWorlds.Message("MSG_" + i, attachments: Array.Empty<MailAttachment>()))
            .ToArray();

        InboxRetention.ToPrune(stored).ShouldBeEmpty(
            "at capacity exactly, nothing is over — the rule fires on the fifty-first message, not " +
            "the fiftieth.");
    }

    [Fact]
    public void A_full_inbox_drops_its_oldest_empty_handed_messages_first()
    {
        var stored = Enumerable.Range(0, InboxRetention.Capacity + 2)
            .Select(i => InboxWorlds.Message(
                "MSG_" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
                attachments: Array.Empty<MailAttachment>(),
                createdAtUtc: Worlds.Start.AddDays(-100 + i)))
            .ToArray();

        InboxRetention.ToPrune(stored).Select(m => m.Value).ShouldBe(
            new[] { "MSG_000", "MSG_001" },
            "oldest first, and exactly as many as the inbox is over — a rule that dropped more " +
            "would take messages the capacity did not require.");
    }

    [Fact]
    public void A_message_still_owing_a_reward_is_never_dropped()
    {
        var owed = InboxWorlds.Message("MSG_owed", createdAtUtc: Worlds.Start.AddYears(-1));
        var stored = new[] { owed }
            .Concat(Enumerable.Range(0, InboxRetention.Capacity)
                .Select(i => InboxWorlds.Message("MSG_" + i, attachments: Array.Empty<MailAttachment>())))
            .ToArray();

        InboxRetention.ToPrune(stored).ShouldNotContain(
            owed.Id,
            "it is the oldest message in the inbox and it holds an unclaimed attachment. Unclaimed " +
            "attachments are never destroyed — the capacity rule takes the empty-handed ones first " +
            "precisely so it never has to.");
    }

    [Fact]
    public void A_message_whose_reward_was_already_paid_may_be_dropped()
    {
        InboxRetention.IsPrunable(
                InboxWorlds.Message("MSG_paid", claimedAtUtc: Worlds.Start))
            .ShouldBeTrue(
                "a claimed message owes nothing. Holding it for ever would make one compensation " +
                "campaign permanently fill half of every player's inbox.");
    }

    [Theory]
    [InlineData(MessageCategory.MODERATION)]
    [InlineData(MessageCategory.ACCOUNT)]
    public void A_record_message_is_never_dropped_even_when_it_owes_nothing(MessageCategory category)
    {
        InboxRetention.IsPrunable(
                InboxWorlds.Message(
                    "MSG_record", category: category, attachments: Array.Empty<MailAttachment>(),
                    neverExpires: true))
            .ShouldBeFalse(
                "they are the record. An empty-handed sanction notice is exactly the shape the " +
                "capacity rule takes first, which is why the exemption has to be checked before it.");
    }

    [Fact]
    public void An_inbox_of_nothing_but_unpaid_rewards_is_allowed_to_exceed_its_capacity()
    {
        var stored = Enumerable.Range(0, InboxRetention.Capacity + 10)
            .Select(i => InboxWorlds.Message("MSG_" + i))
            .ToArray();

        InboxRetention.ToPrune(stored).ShouldBeEmpty(
            "the two rules meet here and the reward wins: capacity prunes, and unclaimed " +
            "attachments are never destroyed. So the honest answer is an inbox over its cap, not a " +
            "player robbed to bring it under.");
    }

    [Fact]
    public void An_empty_handed_message_goes_before_one_that_was_already_paid()
    {
        var stored = new[]
        {
            InboxWorlds.Message(
                "MSG_paid", createdAtUtc: Worlds.Start.AddYears(-5), claimedAtUtc: Worlds.Start),
            InboxWorlds.Message(
                "MSG_news", attachments: Array.Empty<MailAttachment>(),
                createdAtUtc: Worlds.Start.AddDays(-1)),
        }
        .Concat(Enumerable.Range(0, InboxRetention.Capacity)
            .Select(i => InboxWorlds.Message("MSG_keep_" + i)))
        .ToArray();

        InboxRetention.ToPrune(stored).Select(m => m.Value).ShouldBe(
            new[] { "MSG_news", "MSG_paid" },
            "the empty-handed one goes first even though it is five years newer: 'oldest " +
            "non-attachment messages are pruned first' ranks by what the message CARRIES before it " +
            "ranks by age.");
    }
}
