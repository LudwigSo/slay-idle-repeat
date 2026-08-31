using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Services.Inbox;

/// <summary>Sending: what reaches a player's inbox, what never does, and what a full inbox drops for it.</summary>
public sealed class MailSenderTests
{
    private static readonly PlayerId Second = new("PLAYER_inbox-2");

    private static (MailSender Sender, InMemoryMessageRepository Store) World()
    {
        // ONE clock for the store and the sender: the store hides an expired message and the sender
        // stamps the expiry, so two clocks would let a fixture write messages the store then denies
        // exist, and every capacity case below would be measuring an empty inbox.
        var clock = InboxWorlds.Clock();
        var store = new InMemoryMessageRepository(clock);

        return (new MailSender(store, InboxWorlds.Catalogue(), clock, new CountingIdGenerator()), store);
    }

    private static MailSendRequest Request(
        string? templateId = null,
        IReadOnlyDictionary<string, string>? parameters = null,
        IReadOnlyList<MailAttachment>? attachments = null) =>
        new(templateId ?? InboxWorlds.CompensationTemplate,
            parameters ?? InboxWorlds.CompensationParams(),
            attachments ?? new[] { new MailAttachment("SOUL_SHARDS", 500) });

    [Fact]
    public async Task A_send_writes_one_message_per_recipient()
    {
        var (sender, store) = World();

        var result = await sender.SendAsync(
            Request(), new[] { InboxWorlds.Player, Second }, Worlds.Cancel);

        result.Accepted.ShouldBeTrue();
        result.Delivered.Count.ShouldBe(2);
        (await store.GetActiveAsync(InboxWorlds.Player, Worlds.Cancel)).ShouldHaveSingleItem();
        (await store.GetActiveAsync(Second, Worlds.Cancel)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Every_recipient_gets_their_own_message_id()
    {
        var (sender, _) = World();

        var result = await sender.SendAsync(
            Request(), new[] { InboxWorlds.Player, Second }, Worlds.Cancel);

        result.Delivered.Select(m => m.Id).Distinct().Count().ShouldBe(
            2,
            "a grant is idempotent ON the message id, so two players sharing one would mean the " +
            "second player's claim replays the first's — one reward for the cohort.");
    }

    [Fact]
    public async Task The_category_comes_from_the_template_rather_than_from_the_sender()
    {
        var (sender, _) = World();

        var result = await sender.SendAsync(Request(), new[] { InboxWorlds.Player }, Worlds.Cancel);

        result.Delivered.ShouldHaveSingleItem().Category.ShouldBe(
            MessageCategory.COMPENSATION,
            "the category decides whether a message ever expires and whether capacity may prune it. " +
            "An operator picking it per send would be an operator deciding whether a sanction " +
            "notice is permanent.");
    }

    [Fact]
    public async Task An_expiring_category_is_stamped_with_its_expiry()
    {
        var (sender, _) = World();

        var result = await sender.SendAsync(Request(), new[] { InboxWorlds.Player }, Worlds.Cancel);

        result.Delivered.ShouldHaveSingleItem().ExpiresAtUtc.ShouldBe(Worlds.Start.AddDays(30));
    }

    [Fact]
    public async Task A_refused_send_writes_nothing_at_all()
    {
        var (sender, store) = World();

        var result = await sender.SendAsync(
            Request(attachments: new[] { new MailAttachment("CHEST", 1) }),
            new[] { InboxWorlds.Player, Second },
            Worlds.Cancel);

        result.Accepted.ShouldBeFalse();
        result.Refusals.Select(r => r.Refusal).ShouldBe(
            new[] { MailSendRefusal.UNGRANTABLE_ATTACHMENT },
            "a message carrying an attachment the claim path holds for ever is stopped BEFORE it is " +
            "written, rather than after it has reached a cohort.");
        result.Delivered.ShouldBeEmpty();
        (await store.GetActiveAsync(InboxWorlds.Player, Worlds.Cancel)).ShouldBeEmpty(
            "and not to half a cohort either: the whole send is checked before any of it lands.");
    }

    [Fact]
    public async Task A_send_with_no_recipients_writes_nothing_and_is_not_an_error()
    {
        var (sender, _) = World();

        var result = await sender.SendAsync(
            Request(), Array.Empty<PlayerId>(), Worlds.Cancel);

        result.Accepted.ShouldBeTrue(
            "a predicate that selected nobody is a well-formed send with no recipients, which the " +
            "dry run has already reported. Refusing it here would make an empty cohort look like a " +
            "malformed message.");
        result.Delivered.ShouldBeEmpty();
    }

    [Fact]
    public async Task Check_and_SendAsync_refuse_the_same_sends()
    {
        var (sender, _) = World();
        var request = Request(attachments: new[] { new MailAttachment("GEAR", 1) });

        var checkedRefusals = sender.Check(request).Select(r => r.Refusal).ToArray();
        var sent = await sender.SendAsync(request, new[] { InboxWorlds.Player }, Worlds.Cancel);

        // 🔴 The floor before the differential: both sides are computed by the code under test, so
        // the day GEAR becomes grantable they are two empty lists that agree perfectly and this case
        // reports "the two paths refuse alike" over a send that refuses nothing.
        checkedRefusals.ShouldBe(
            [MailSendRefusal.UNGRANTABLE_ATTACHMENT],
            "this request attaches GEAR, which nothing can grant. If that stops being a refusal, " +
            "pick another unrepresentable attachment rather than letting the comparison below run " +
            "over two empty lists.");

        sent.Refusals.Select(r => r.Refusal).ShouldBe(
            checkedRefusals,
            "the dry run answers the same question the real send does out of the same code. A tool " +
            "that checked differently would report a clean dry run for a send that then refuses.");
    }

    [Fact]
    public async Task A_send_into_a_full_inbox_prunes_to_make_room()
    {
        var (sender, store) = World();

        // Minutes apart rather than days: all fifty have to still be LIVE, and thirty days of
        // retention means a fixture spaced by days would file most of them as expired — leaving the
        // capacity rule nothing to fire on and this case green over an empty inbox.
        for (var i = 0; i < InboxRetention.Capacity; i++)
        {
            await store.AppendAsync(
                InboxWorlds.Message(
                    "MSG_old_" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
                    attachments: Array.Empty<MailAttachment>(),
                    createdAtUtc: Worlds.Start.AddMinutes(-1000 + i)),
                Worlds.Cancel);
        }

        await sender.SendAsync(Request(), new[] { InboxWorlds.Player }, Worlds.Cancel);

        var stored = await store.GetActiveAsync(InboxWorlds.Player, Worlds.Cancel);

        stored.Count.ShouldBe(InboxRetention.Capacity);
        stored.ShouldNotContain(
            m => m.Id.Value == "MSG_old_000",
            "the oldest empty-handed message went to make room, which is what the capacity rule is.");
        stored.ShouldContain(
            m => m.Attachments.Count > 0,
            "and the message that arrived is the one still there — pruning before the append would " +
            "have made room for a send that then failed.");
    }

    [Fact]
    public async Task A_send_into_a_full_inbox_of_unpaid_rewards_prunes_nothing_and_still_delivers()
    {
        var (sender, store) = World();

        for (var i = 0; i < InboxRetention.Capacity; i++)
        {
            await store.AppendAsync(
                InboxWorlds.Message("MSG_owed_" + i, createdAtUtc: Worlds.Start.AddMinutes(-1000 + i)),
                Worlds.Cancel);
        }

        await sender.SendAsync(Request(), new[] { InboxWorlds.Player }, Worlds.Cancel);

        (await store.GetActiveAsync(InboxWorlds.Player, Worlds.Cancel)).Count.ShouldBe(
            InboxRetention.Capacity + 1,
            "the inbox is allowed over its cap rather than a player being robbed to bring it under. " +
            "Refusing the send instead would let a full inbox block the compensation for the " +
            "incident that filled it.");
    }
}
