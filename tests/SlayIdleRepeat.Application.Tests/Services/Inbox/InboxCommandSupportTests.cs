using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Services.Inbox;

/// <summary>The two halves of a claim this layer owns: loading the projection, and stamping what it paid.</summary>
public sealed class InboxCommandSupportTests
{
    private static (InboxCommandSupport Support, InMemoryMessageRepository Store) World()
    {
        var store = new InMemoryMessageRepository(InboxWorlds.Clock());

        return (new InboxCommandSupport(store), store);
    }

    [Fact]
    public void Only_the_claim_command_reads_the_inbox()
    {
        InboxCommandSupport.Reads(new ClaimInboxCommand()).ShouldBeTrue();
        InboxCommandSupport.Reads(new RollDiceCommand()).ShouldBeFalse(
            "the inbox is a second table. Reading it on every roll of the dice would put a query on " +
            "the hottest path in the game to serve one command.");
    }

    [Fact]
    public async Task A_player_with_no_messages_loads_the_empty_inbox_rather_than_nothing()
    {
        var (support, _) = World();

        (await support.LoadAsync(InboxWorlds.Player, Worlds.Cancel)).ShouldBe(
            InboxView.Empty,
            "the domain treats a MISSING projection as a loading defect, so 'this player has no " +
            "messages' has to be a real, empty value rather than the absence of one.");
    }

    [Fact]
    public async Task The_projection_carries_what_the_rules_read_and_nothing_more()
    {
        var (support, store) = World();
        await store.AppendAsync(InboxWorlds.Message("MSG_a"), Worlds.Cancel);

        var message = (await support.LoadAsync(InboxWorlds.Player, Worlds.Cancel))
            .Messages.ShouldHaveSingleItem();

        message.Id.Value.ShouldBe("MSG_a");
        message.Category.ShouldBe(MessageCategory.COMPENSATION);
        message.Attachments.ShouldHaveSingleItem().Type.ShouldBe("SOUL_SHARDS");
        message.IsClaimable.ShouldBeTrue();
    }

    [Fact]
    public async Task Another_players_messages_are_not_in_the_projection()
    {
        var (support, store) = World();
        await store.AppendAsync(
            InboxWorlds.Message("MSG_theirs", player: new PlayerId("PLAYER_other")), Worlds.Cancel);

        (await support.LoadAsync(InboxWorlds.Player, Worlds.Cancel)).Messages.ShouldBeEmpty();
    }

    [Fact]
    public void The_claimed_ids_are_read_off_the_events_the_domain_produced()
    {
        var events = new DomainEvent[]
        {
            new CurrencyChanged(1, CurrencyId.SOUL_SHARDS, 500, "mail_attachment"),
            new MailClaimed(2, new MessageId("MSG_a"), MessageCategory.COMPENSATION),
            new MailClaimed(3, new MessageId("MSG_b"), MessageCategory.RECONCILIATION),
        };

        InboxCommandSupport.ClaimedIn(events).Select(m => m.Value).ShouldBe(
            new[] { "MSG_a", "MSG_b" },
            "the event IS the instruction to stamp, so 'claimed' and 'granted' stay the same fact " +
            "rather than two that can drift.");
    }

    [Fact]
    public void A_batch_that_claimed_nothing_names_no_messages()
    {
        InboxCommandSupport.ClaimedIn(
                new DomainEvent[] { new CurrencyChanged(1, CurrencyId.CROWNS, 10, "run_payout") })
            .ShouldBeEmpty(
                "the negative control: a reader that named a message for every batch would stamp a " +
                "message the run payout happened to be near.");
    }

    [Fact]
    public void A_batch_names_exactly_the_messages_it_claimed_and_no_neighbour()
    {
        InboxCommandSupport.ClaimedIn(new DomainEvent[]
            {
                new MailClaimed(1, new MessageId("MSG_paid"), MessageCategory.COMPENSATION),
                new CurrencyChanged(2, CurrencyId.CROWNS, 500, "mail_claim"),
            })
            .Select(id => id.Value)
            .ShouldBe(new[] { "MSG_paid" },
                "a message the batch did not claim must not be named by the same read — that is a " +
                "reward taken away without ever being paid.");
    }
}
