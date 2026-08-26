using Shouldly;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Analytics;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Commands;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Analytics;

/// <summary>
/// The sink over the analytics port: every translated event is tracked, and every one against the
/// batch's own player — attribution is the reason the batch carries a player at all.
/// </summary>
public sealed class AnalyticsEventSinkTests
{
    /// <summary>A port double that keeps every tracked call.</summary>
    private sealed class CapturingAnalyticsPort : IAnalyticsSinkPort
    {
        internal List<(PlayerId Player, AnalyticsEvent Event)> Tracked { get; } = [];

        public void Track(PlayerId player, AnalyticsEvent analyticsEvent) =>
            Tracked.Add((player, analyticsEvent));
    }

    [Fact]
    public async Task ReceiveAsync_tracks_every_translated_event_against_the_batchs_player()
    {
        var game = Worlds.Game();
        var player = game.CreatePlayer();
        var batch = AnalyticsWorlds.Sent(
            game, player, new StartRunCommand(Worlds.Chapter, DifficultyTier.NORMAL));
        var port = new CapturingAnalyticsPort();

        await new AnalyticsEventSink(port).ReceiveAsync(batch, Worlds.Cancel);

        port.Tracked.Select(t => t.Event.Name).ShouldContain(
            AnalyticsVocabulary.RunStart, "an accepted START_RUN translates to run_start.");
        port.Tracked.ShouldAllBe(
            t => t.Player == player,
            "every tracked event belongs to the batch's player. An event tracked against anyone " +
            "else is another player's behaviour in this player's funnel.");
    }

    [Fact]
    public async Task ReceiveAsync_tracks_nothing_for_a_batch_that_translates_to_nothing()
    {
        var (game, player) = Worlds.InARun();
        var batch = new DispatchedEvents(
            player,
            new RollDiceCommand(),
            game.State(player),
            [new PityCounterAdvanced(1, "elite_mercy", 2)]);
        var port = new CapturingAnalyticsPort();

        await new AnalyticsEventSink(port).ReceiveAsync(batch, Worlds.Cancel);

        port.Tracked.ShouldBeEmpty("nothing in this batch has an authored analytics name.");
    }

    [Fact]
    public async Task ReceiveAsync_refuses_a_null_batch()
    {
        var sink = new AnalyticsEventSink(new CapturingAnalyticsPort());

        await Should.ThrowAsync<ArgumentNullException>(() => sink.ReceiveAsync(null!, Worlds.Cancel));
    }

    [Fact]
    public void Constructor_refuses_a_null_port()
    {
        Should.Throw<ArgumentNullException>(() => new AnalyticsEventSink(null!));
    }
}
