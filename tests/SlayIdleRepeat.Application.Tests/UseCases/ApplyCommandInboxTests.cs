using Shouldly;
using SlayIdleRepeat.Adapters.InMemory;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Tests.Services.Inbox;
using SlayIdleRepeat.Application.UseCases;
using SlayIdleRepeat.Core;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.UseCases;

/// <summary>
/// The claim as one commit: the projection is loaded before the rules see it, and what the rules
/// paid is stamped after the player's own state is safe.
/// </summary>
public sealed class ApplyCommandInboxTests
{
    private sealed record World(
        ApplyCommandUseCase UseCase,
        InMemoryMessageRepository Messages,
        WorldSliceStore Store,
        InMemoryGameHarness Harness);

    private sealed record InMemoryGameHarness(PlayerId Player, GameContext Context);

    private static async Task<World> WorldAsync(bool withInbox = true)
    {
        var game = Worlds.Game();
        var player = game.CreatePlayer();
        var cache = Worlds.CacheHolding(game.State(player));
        var store = new WorldSliceStore(cache);
        var messages = new InMemoryMessageRepository(InboxWorlds.Clock());

        await Task.CompletedTask;

        return new World(
            new ApplyCommandUseCase(
                store,
                new DomainEventDispatcher([]),
                withInbox ? new InboxCommandSupport(messages) : null),
            messages,
            store,
            new InMemoryGameHarness(player, Worlds.Context(game, Worlds.MetaSeed)));
    }

    private static async Task<long> ShardsAsync(World world)
    {
        var rows = await world.Store.ReadSnapshotsAsync(world.Harness.Player, Worlds.Cancel);
        var player = Player.Rehydrate(rows!.Player, Worlds.Content);

        return player.IsSuccess
            ? player.Value.BalanceOf(CurrencyId.SOUL_SHARDS)
            : throw new InvalidOperationException("the committed player does not rehydrate: " + player.Error);
    }

    [Fact]
    public async Task A_claim_commits_the_grant_and_stamps_the_message()
    {
        var world = await WorldAsync();
        await world.Messages.AppendAsync(
            InboxWorlds.Message("MSG_a", player: world.Harness.Player), Worlds.Cancel);
        var before = await ShardsAsync(world);

        var outcome = await world.UseCase.ExecuteAsync(
            new ApplyCommandRequest(world.Harness.Player, null, new ClaimInboxCommand()),
            world.Harness.Context,
            Worlds.Cancel);

        outcome.Accepted.ShouldBeTrue();
        (await ShardsAsync(world)).ShouldBe(
            before + 500,
            "the grant is on the player's committed row, not only in the returned state.");
        (await world.Messages.GetActiveAsync(world.Harness.Player, Worlds.Cancel))
            .ShouldHaveSingleItem().ClaimedAtUtc.ShouldNotBeNull(
                "and the message is spent, so the next command pays nothing.");
    }

    [Fact]
    public async Task A_second_claim_pays_nothing_more()
    {
        var world = await WorldAsync();
        await world.Messages.AppendAsync(
            InboxWorlds.Message("MSG_a", player: world.Harness.Player), Worlds.Cancel);

        await world.UseCase.ExecuteAsync(
            new ApplyCommandRequest(world.Harness.Player, null, new ClaimInboxCommand()),
            world.Harness.Context, Worlds.Cancel);
        var afterFirst = await ShardsAsync(world);

        var second = await world.UseCase.ExecuteAsync(
            new ApplyCommandRequest(world.Harness.Player, null, new ClaimInboxCommand()),
            world.Harness.Context, Worlds.Cancel);

        second.Accepted.ShouldBeTrue();
        (await ShardsAsync(world)).ShouldBe(
            afterFirst,
            "the stamp written by the first claim is the only thing between a resent CLAIM_INBOX " +
            "under a NEW command id and a second payout. The ledger replays the same id; it cannot " +
            "help with a different one.");
    }

    [Fact]
    public async Task A_command_that_does_not_read_the_inbox_leaves_it_untouched()
    {
        var world = await WorldAsync();
        await world.Messages.AppendAsync(
            InboxWorlds.Message("MSG_a", player: world.Harness.Player), Worlds.Cancel);

        await world.UseCase.ExecuteAsync(
            new ApplyCommandRequest(
                world.Harness.Player, null, new BeginSessionCommand("test", "test")),
            world.Harness.Context,
            Worlds.Cancel);

        (await world.Messages.GetActiveAsync(world.Harness.Player, Worlds.Cancel))
            .ShouldHaveSingleItem().ClaimedAtUtc.ShouldBeNull(
                "the negative control: a layer that stamped on every accepted command would spend " +
                "every message the first time a player logged in.");
    }

    [Fact]
    public async Task A_process_with_no_message_store_faults_the_claim_rather_than_answering_it()
    {
        var world = await WorldAsync(withInbox: false);

        await Should.ThrowAsync<InvalidOperationException>(
            async () => await world.UseCase.ExecuteAsync(
                new ApplyCommandRequest(world.Harness.Player, null, new ClaimInboxCommand()),
                world.Harness.Context,
                Worlds.Cancel));
    }
}
