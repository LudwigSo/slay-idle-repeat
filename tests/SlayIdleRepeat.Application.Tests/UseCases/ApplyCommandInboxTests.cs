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
/// The half of a claim this use case owns: the projection is loaded before the rules see it, and a
/// process with no store faults rather than answering "you have no messages".
/// </summary>
/// <remarks>
/// 🔒 The STAMP is deliberately not tested here, because it is deliberately not done here. It rides
/// the accepted command's transaction on <c>CommandCommit.Claim</c>, so the cases that assert a
/// message is spent live where the commit is built — <c>CommandGatewayInboxClaimTests</c> end to
/// end, and <c>IUnitOfWorkContractTests</c> for the all-or-nothing shape against a raised fault.
/// A case here that reached into the message store would be asserting on a write this path does not
/// make.
/// </remarks>
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
    public async Task A_claim_grants_onto_the_players_committed_row()
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
            "the loaded projection is what the rules read the attachment off, so a claim that " +
            "granted nothing would mean the inbox never reached them.");
    }

    [Fact]
    public async Task A_command_that_does_not_read_the_inbox_loads_no_projection()
    {
        var world = await WorldAsync();
        await world.Messages.AppendAsync(
            InboxWorlds.Message("MSG_a", player: world.Harness.Player), Worlds.Cancel);

        var outcome = await world.UseCase.ExecuteAsync(
            new ApplyCommandRequest(
                world.Harness.Player, null, new BeginSessionCommand("test", "test")),
            world.Harness.Context,
            Worlds.Cancel);

        // Null rather than InboxView.Empty, and the difference is the point: an empty projection is
        // the answer "this player has no messages", which only a command that ASKED may be given.
        outcome.State.Inbox.ShouldBeNull(
            "the negative control: the inbox is a second table, and a layer that loaded it on every " +
            "command would put a query on the hottest path in the game to serve one of them.");
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
