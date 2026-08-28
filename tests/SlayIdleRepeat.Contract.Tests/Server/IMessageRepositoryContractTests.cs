using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Contract.Tests.Server;

/// <summary>
/// States what <see cref="IMessageRepository"/> <em>means</em>: live messages by owner, an append
/// that is idempotent on the message id, a stamp that keeps the first moment, and an expiry read
/// that removes nothing.
/// </summary>
/// <remarks>
/// <para>
/// The cases are written around the two things a wrong implementation destroys: a reward paid twice
/// (an append that overwrites, a stamp that clears) and a reward destroyed (an expiry read that
/// deletes, a live read that hides an unexpired message). Everything else this port does is
/// arithmetic on top of those two.
/// </para>
/// <para>
/// Expiry is judged against the implementation's own clock for the live read and against the
/// caller's instant for the expiry read, and the suite exercises both: a store that used one for
/// both would either show a player a message that has gone or hide one that has not.
/// </para>
/// </remarks>
[ContractSuiteFor(typeof(IMessageRepository))]
public abstract class IMessageRepositoryContractTests
{
    /// <summary>The instant every fixture's clock is set to, so "an hour ago" is a real fact.</summary>
    protected static readonly DateTimeOffset Now = new(2026, 8, 29, 5, 0, 0, TimeSpan.Zero);

    private static readonly PlayerId Player = new("PLAYER_message-suite");
    private static readonly PlayerId Other = new("PLAYER_message-suite-other");

    /// <summary>A store under test, over a backing of its own, whose clock reads <see cref="Now"/>.</summary>
    protected abstract IMessageRepository Create();

    /// <summary>Builds a message. Every member the cases vary is a named parameter.</summary>
    protected static PlayerMessage Message(
        string id,
        PlayerId? player = null,
        MessageCategory category = MessageCategory.COMPENSATION,
        long soulShards = 500,
        DateTimeOffset? createdAtUtc = null,
        DateTimeOffset? expiresAtUtc = null,
        DateTimeOffset? claimedAtUtc = null,
        bool neverExpires = false) =>
        new(new MessageId(id),
            player ?? Player,
            category,
            "loc.mail.compensation.outage.body",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["dateUtc"] = "2026-03-14",
                ["hours"] = "3",
            },
            soulShards == 0
                ? Array.Empty<MailAttachment>()
                : new[] { new MailAttachment("SOUL_SHARDS", soulShards) },
            createdAtUtc ?? Now.AddDays(-1),
            // 🔒 A separate flag rather than a null expiry, because the default IS a null: passing
            // `expiresAtUtc: null` would silently fall through to the 29-day default, and the two
            // cases that turn on "never expires" would have been testing an ordinary message.
            neverExpires ? null : expiresAtUtc ?? Now.AddDays(29),
            ReadAtUtc: null,
            claimedAtUtc);

    [Fact]
    public async Task A_player_with_no_messages_has_an_empty_inbox()
    {
        var store = Create();

        (await store.GetActiveAsync(Player, PersistenceWorlds.Cancel)).ShouldBeEmpty(
            "a player with no messages has an empty inbox, not a null one — a null would make the "
            + "claim path's 'nothing loaded' and 'nothing to claim' the same value.");
    }

    [Fact]
    public async Task An_appended_message_reads_back_whole()
    {
        var store = Create();
        var message = Message("MSG_whole");

        await store.AppendAsync(message, PersistenceWorlds.Cancel);

        (await store.GetActiveAsync(Player, PersistenceWorlds.Cancel)).ShouldBe(
            new[] { message },
            "every field of the row is load-bearing: the template and its parameters are what the "
            + "message SAYS, the attachments are what it owes, and the two timestamps are what "
            + "decides whether it is still live and still unpaid.");
    }

    [Fact]
    public async Task A_message_is_invisible_to_every_other_player()
    {
        var store = Create();
        await store.AppendAsync(Message("MSG_mine"), PersistenceWorlds.Cancel);

        (await store.GetActiveAsync(Other, PersistenceWorlds.Cancel)).ShouldBeEmpty(
            "an inbox is one player's. A message visible to a second player is a reward two "
            + "accounts can both claim.");
    }

    [Fact]
    public async Task Appending_a_message_id_twice_leaves_the_first_row_untouched()
    {
        var store = Create();
        var first = Message("MSG_twice", soulShards: 500);
        await store.AppendAsync(first, PersistenceWorlds.Cancel);
        await store.MarkClaimedAsync(
            Player, new[] { first.Id }, PersistenceWorlds.Cancel);

        await store.AppendAsync(Message("MSG_twice", soulShards: 999), PersistenceWorlds.Cancel);

        var stored = (await store.GetActiveAsync(Player, PersistenceWorlds.Cancel)).ShouldHaveSingleItem();
        stored.Attachments.ShouldBe(first.Attachments,
            "the append is idempotent on the message id: a retried segment send must leave the half "
            + "that landed exactly as it was.");
        stored.ClaimedAtUtc.ShouldNotBeNull(
            "…and the claimed stamp above all, because an overwrite that cleared it would pay the "
            + "same 500 Soul Shards a second time.");
    }

    [Fact]
    public async Task Marking_claimed_stamps_the_message()
    {
        var store = Create();
        var message = Message("MSG_claim");
        await store.AppendAsync(message, PersistenceWorlds.Cancel);

        await store.MarkClaimedAsync(Player, new[] { message.Id }, PersistenceWorlds.Cancel);

        (await store.GetActiveAsync(Player, PersistenceWorlds.Cancel))
            .ShouldHaveSingleItem().ClaimedAtUtc.ShouldNotBeNull(
                "an unstamped message is one the next claim pays again.");
    }

    [Fact]
    public async Task Marking_an_already_claimed_message_keeps_the_first_moment()
    {
        var store = Create();
        var message = Message("MSG_restamp");
        await store.AppendAsync(message, PersistenceWorlds.Cancel);
        await store.MarkClaimedAsync(Player, new[] { message.Id }, PersistenceWorlds.Cancel);
        var first = (await store.GetActiveAsync(Player, PersistenceWorlds.Cancel))
            .ShouldHaveSingleItem().ClaimedAtUtc;

        await store.MarkClaimedAsync(Player, new[] { message.Id }, PersistenceWorlds.Cancel);

        (await store.GetActiveAsync(Player, PersistenceWorlds.Cancel))
            .ShouldHaveSingleItem().ClaimedAtUtc.ShouldBe(
                first,
                "the timestamp records when the reward was actually paid. A re-stamp that moved it "
                + "forward would date every payment to whichever retry happened last.");
    }

    [Fact]
    public async Task Marking_a_message_the_player_does_not_own_stamps_nothing_and_throws_nothing()
    {
        var store = Create();
        var message = Message("MSG_not-yours");
        await store.AppendAsync(message, PersistenceWorlds.Cancel);

        await store.MarkClaimedAsync(Other, new[] { message.Id }, PersistenceWorlds.Cancel);

        (await store.GetActiveAsync(Player, PersistenceWorlds.Cancel))
            .ShouldHaveSingleItem().ClaimedAtUtc.ShouldBeNull(
                "another player's stamp must not spend this player's message.");
    }

    [Fact]
    public async Task Marking_an_unknown_message_creates_nothing()
    {
        var store = Create();

        await store.MarkClaimedAsync(
            Player, new[] { new MessageId("MSG_never") }, PersistenceWorlds.Cancel);

        (await store.GetActiveAsync(Player, PersistenceWorlds.Cancel)).ShouldBeEmpty(
            "a stamp for an id nothing sent would be a claimed message nobody wrote — and the claim "
            + "path names ids the player gave it, so this is reachable from the wire.");
    }

    [Fact]
    public async Task An_expired_message_is_no_longer_in_the_inbox()
    {
        var store = Create();

        await store.AppendAsync(
            Message("MSG_expired", expiresAtUtc: Now.AddHours(-1)), PersistenceWorlds.Cancel);

        (await store.GetActiveAsync(Player, PersistenceWorlds.Cancel)).ShouldBeEmpty(
            "the message disappears when it expires. A store that kept showing it would leave the "
            + "player looking at a message the expiry job has already emptied.");
    }

    [Fact]
    public async Task A_message_with_no_expiry_is_live_for_ever()
    {
        var store = Create();
        var record = Message(
            "MSG_record", category: MessageCategory.MODERATION, soulShards: 0,
            createdAtUtc: Now.AddYears(-3), neverExpires: true);

        await store.AppendAsync(record, PersistenceWorlds.Cancel);

        (await store.GetActiveAsync(Player, PersistenceWorlds.Cancel)).ShouldHaveSingleItem().Id
            .ShouldBe(record.Id,
                "a null expiry is what makes the moderation and account categories the record. A "
                + "store that treated it as 'expired at the epoch' would delete exactly the messages "
                + "that must never be deleted.");
    }

    [Fact]
    public async Task The_expiry_read_answers_the_due_messages_and_removes_none_of_them()
    {
        var store = Create();
        var due = Message("MSG_due", expiresAtUtc: Now.AddHours(-1));
        await store.AppendAsync(due, PersistenceWorlds.Cancel);

        (await store.DequeueExpiringAsync(Now, 10, PersistenceWorlds.Cancel))
            .ShouldHaveSingleItem().Id.ShouldBe(due.Id);

        (await store.DequeueExpiringAsync(Now, 10, PersistenceWorlds.Cancel))
            .ShouldHaveSingleItem().Id.ShouldBe(
                due.Id,
                "the read must not remove. The job grants what the message was holding and only "
                + "THEN deletes it, so a store that dequeued on read would destroy every unclaimed "
                + "reward the job crashed halfway through.");
    }

    [Fact]
    public async Task The_expiry_read_never_answers_a_message_with_no_expiry()
    {
        var store = Create();
        await store.AppendAsync(
            Message("MSG_permanent", category: MessageCategory.ACCOUNT, soulShards: 0, neverExpires: true),
            PersistenceWorlds.Cancel);

        (await store.DequeueExpiringAsync(Now.AddYears(50), 10, PersistenceWorlds.Cancel)).ShouldBeEmpty(
            "the record never falls due, at any instant. This is what lets the job stay ignorant of "
            + "which categories are permanent.");
    }

    [Fact]
    public async Task The_expiry_read_answers_across_players_oldest_expiry_first_and_honours_its_limit()
    {
        var store = Create();
        await store.AppendAsync(
            Message("MSG_second", expiresAtUtc: Now.AddHours(-1)), PersistenceWorlds.Cancel);
        await store.AppendAsync(
            Message("MSG_first", player: Other, expiresAtUtc: Now.AddDays(-9)), PersistenceWorlds.Cancel);

        var batch = await store.DequeueExpiringAsync(Now, 1, PersistenceWorlds.Cancel);

        batch.ShouldHaveSingleItem().Id.Value.ShouldBe(
            "MSG_first",
            "the job sweeps every player's inbox and takes the longest-overdue first. A read that "
            + "ordered by anything else would leave the oldest reward unpaid for as long as newer "
            + "ones keep arriving.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_non_positive_expiry_batch_is_refused(int limit)
    {
        var store = Create();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await store.DequeueExpiringAsync(Now, limit, PersistenceWorlds.Cancel));
    }

    [Fact]
    public async Task Deleting_removes_the_named_messages_and_nothing_else()
    {
        var store = Create();
        await store.AppendAsync(Message("MSG_goes"), PersistenceWorlds.Cancel);
        await store.AppendAsync(Message("MSG_stays"), PersistenceWorlds.Cancel);

        await store.DeleteAsync(new[] { new MessageId("MSG_goes") }, PersistenceWorlds.Cancel);

        (await store.GetActiveAsync(Player, PersistenceWorlds.Cancel))
            .ShouldHaveSingleItem().Id.Value.ShouldBe("MSG_stays");
    }

    [Fact]
    public async Task Deleting_nothing_is_a_no_op()
    {
        var store = Create();
        await store.AppendAsync(Message("MSG_kept"), PersistenceWorlds.Cancel);

        await store.DeleteAsync(Array.Empty<MessageId>(), PersistenceWorlds.Cancel);

        (await store.GetActiveAsync(Player, PersistenceWorlds.Cancel)).ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_null_message_is_a_null_argument_fault()
    {
        var store = Create();

        await Should.ThrowAsync<ArgumentNullException>(
            async () => await store.AppendAsync(null!, PersistenceWorlds.Cancel));
    }

    [Fact]
    public async Task An_already_cancelled_token_is_observed_by_every_method()
    {
        var store = Create();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.GetActiveAsync(Player, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.AppendAsync(Message("MSG_cancelled"), source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.MarkClaimedAsync(
                Player, new[] { new MessageId("MSG_cancelled") }, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.DequeueExpiringAsync(Now, 10, source.Token));

        await Should.ThrowAsync<OperationCanceledException>(
            async () => await store.DeleteAsync(
                new[] { new MessageId("MSG_cancelled") }, source.Token));
    }
}
