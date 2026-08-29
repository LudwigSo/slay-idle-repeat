using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Analytics;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Services.Inbox;

/// <summary>
/// The nightly sweep: unclaimed attachments are auto-granted when the message expires, never
/// destroyed.
/// </summary>
public sealed class InboxExpiryJobTests
{
    /// <summary>The instant the sweep judges expiry against — a day past every fixture's expiry.</summary>
    private static readonly DateTimeOffset Overdue = Worlds.Start.AddDays(60);

    private sealed record Sweeper(
        InboxExpiryJob Job,
        InMemoryMessageRepository Messages,
        InMemoryPlayerRepository Players,
        RecordingAnalyticsSink Analytics,
        PlayerId Player);

    /// <summary>A real player, stored, with a real message about to expire on them.</summary>
    private static async Task<Sweeper> WorldAsync(params PlayerMessage[] messages)
    {
        var game = Worlds.Game();
        var player = game.CreatePlayer();
        var slice = game.State(player);

        var players = new InMemoryPlayerRepository();
        await players.CreateAnonymousAsync(
            new PlayerProfile(slice.Player.ToSnapshot(), null), Worlds.Cancel);

        var store = new InMemoryMessageRepository(InboxWorlds.Clock(Overdue));

        foreach (var message in messages)
        {
            await store.AppendAsync(message with { Player = player }, Worlds.Cancel);
        }

        var analytics = new RecordingAnalyticsSink();

        return new Sweeper(
            new InboxExpiryJob(store, players, analytics, Worlds.Content), store, players, analytics, player);
    }

    private static async Task<long> ShardsAsync(Sweeper world)
    {
        var profile = await world.Players.GetAsync(world.Player, Worlds.Cancel);
        var player = Player.Rehydrate(profile!.Player, Worlds.Content);

        return player.IsSuccess
            ? player.Value.BalanceOf(CurrencyId.SOUL_SHARDS)
            : throw new InvalidOperationException("the swept player does not rehydrate: " + player.Error);
    }

    [Fact]
    public async Task An_expiring_message_pays_out_before_it_disappears()
    {
        var world = await WorldAsync(InboxWorlds.Message("MSG_due"));
        var before = await ShardsAsync(world);

        var sweep = await world.Job.SweepAsync(Overdue, 10, Worlds.Cancel);

        sweep.AutoGranted.ShouldBe(1);
        (await ShardsAsync(world)).ShouldBe(
            before + 500,
            "the message disappears; the reward does not. This game does not have expiring gifts.");
        (await world.Messages.GetActiveAsync(world.Player, Worlds.Cancel)).ShouldBeEmpty();
    }

    [Fact]
    public async Task The_swept_message_is_removed()
    {
        var world = await WorldAsync(InboxWorlds.Message("MSG_due"));

        var sweep = await world.Job.SweepAsync(Overdue, 10, Worlds.Cancel);

        sweep.Deleted.ShouldBe(1);
        (await world.Messages.DequeueExpiringAsync(Overdue, 10, Worlds.Cancel)).ShouldBeEmpty(
            "a message that was paid and left standing is one the next sweep pays again.");
    }

    [Fact]
    public async Task A_message_already_paid_is_removed_without_being_paid_again()
    {
        var world = await WorldAsync(
            InboxWorlds.Message("MSG_spent", claimedAtUtc: Worlds.Start));
        var before = await ShardsAsync(world);

        var sweep = await world.Job.SweepAsync(Overdue, 10, Worlds.Cancel);

        sweep.AutoGranted.ShouldBe(0);
        sweep.Deleted.ShouldBe(1);
        (await ShardsAsync(world)).ShouldBe(
            before,
            "the stamp is what a crash between the grant and the delete leaves behind, and the next " +
            "sweep must read it as 'paid' rather than paying a second time.");
    }

    [Fact]
    public async Task A_message_holding_nothing_is_removed_without_a_grant()
    {
        var world = await WorldAsync(InboxWorlds.Message(
            "MSG_news", category: MessageCategory.ANNOUNCEMENT,
            attachments: Array.Empty<MailAttachment>()));

        var sweep = await world.Job.SweepAsync(Overdue, 10, Worlds.Cancel);

        sweep.AutoGranted.ShouldBe(0);
        sweep.Deleted.ShouldBe(1);
        sweep.Withholdings.ShouldBeEmpty(
            "an announcement owes nothing, so removing it withholds nothing — that is a different " +
            "answer from a reward this build could not pay.");
    }

    [Fact]
    public async Task A_message_this_build_cannot_pay_is_removed_and_reported_by_name()
    {
        var world = await WorldAsync(InboxWorlds.Message(
            "MSG_chest", attachments: new[] { new MailAttachment("CHEST", 1) }));

        var sweep = await world.Job.SweepAsync(Overdue, 10, Worlds.Cancel);

        sweep.AutoGranted.ShouldBe(0);
        sweep.Withholdings.ShouldHaveSingleItem().Refusal.ShouldBe(
            MailAttachmentRefusal.CONTAINER_SHELF_ABSENT,
            "a reward that was owed and could not be paid is the one outcome of this job somebody " +
            "has to see. Keeping the row instead would make the first such attachment an inbox " +
            "message nothing can ever remove.");
    }

    [Fact]
    public async Task A_message_that_has_not_expired_is_left_alone()
    {
        var world = await WorldAsync(InboxWorlds.Message("MSG_live"));

        var sweep = await world.Job.SweepAsync(Worlds.Start, 10, Worlds.Cancel);

        sweep.Examined.ShouldBe(0);
        sweep.Deleted.ShouldBe(0);
    }

    [Fact]
    public async Task A_message_that_never_expires_is_never_swept()
    {
        var world = await WorldAsync(InboxWorlds.Message(
            "MSG_record", category: MessageCategory.MODERATION,
            attachments: Array.Empty<MailAttachment>(), neverExpires: true));

        var sweep = await world.Job.SweepAsync(Overdue.AddYears(50), 10, Worlds.Cancel);

        sweep.Examined.ShouldBe(
            0,
            "the record is permanent at every instant. A job that swept it would delete exactly the " +
            "messages that must never be deleted, fifty years late and silently.");
    }

    [Fact]
    public async Task Each_auto_grant_is_reported_to_analytics_against_its_player()
    {
        var world = await WorldAsync(InboxWorlds.Message("MSG_due"));

        await world.Job.SweepAsync(Overdue, 10, Worlds.Cancel);

        var tracked = world.Analytics.Tracked.ShouldHaveSingleItem();
        tracked.Player.ShouldBe(world.Player);
        tracked.Event.Name.ShouldBe(AnalyticsVocabulary.MailExpiredAutogranted);
        tracked.Event.Properties["message_id"].ShouldBe("MSG_due");
    }

    [Fact]
    public async Task Nothing_is_reported_for_a_message_that_was_not_paid()
    {
        var world = await WorldAsync(InboxWorlds.Message(
            "MSG_chest", attachments: new[] { new MailAttachment("CHEST", 1) }));

        await world.Job.SweepAsync(Overdue, 10, Worlds.Cancel);

        world.Analytics.Tracked.ShouldBeEmpty(
            "the negative control: a job that tracked every examined message would report a grant " +
            "that never happened, and the dashboard would show the reward as delivered.");
    }

    [Fact]
    public async Task The_batch_limit_bounds_one_sweep()
    {
        var world = await WorldAsync(
            InboxWorlds.Message("MSG_a"), InboxWorlds.Message("MSG_b"), InboxWorlds.Message("MSG_c"));

        var sweep = await world.Job.SweepAsync(Overdue, 2, Worlds.Cancel);

        sweep.Examined.ShouldBe(2);
        (await world.Messages.DequeueExpiringAsync(Overdue, 10, Worlds.Cancel)).ShouldHaveSingleItem(
            "what the batch did not reach is still due, so the caller sweeps again rather than the " +
            "remainder waiting a day.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_sweep_that_would_take_no_messages_is_refused(int limit)
    {
        var world = await WorldAsync(InboxWorlds.Message("MSG_due"));

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await world.Job.SweepAsync(Overdue, limit, Worlds.Cancel));
    }

    [Fact]
    public async Task A_message_owed_to_a_player_with_no_row_stops_the_sweep_by_name()
    {
        var store = new InMemoryMessageRepository(InboxWorlds.Clock(Overdue));
        await store.AppendAsync(InboxWorlds.Message("MSG_orphan"), Worlds.Cancel);

        var job = new InboxExpiryJob(
            store, new InMemoryPlayerRepository(), new RecordingAnalyticsSink(), Worlds.Content);

        (await Should.ThrowAsync<InvalidOperationException>(
                async () => await job.SweepAsync(Overdue, 10, Worlds.Cancel)))
            .Message.ShouldContain(
                "MSG_orphan",
                Case.Sensitive,
                "paying it would have to invent the account and skipping it silently would destroy " +
                "a reward, so the sweep says which message and which player instead.");
    }
}
