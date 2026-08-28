using Shouldly;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Tests.UseCases;
using SlayIdleRepeat.Core.Commands;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Events;

/// <summary>
/// The batch's own guards: a delivery with a hole in it fails where it is built, not inside a sink
/// whose failure would be reported against a command that already committed.
/// </summary>
public sealed class DispatchedEventsTests
{
    [Fact]
    public void Construction_refuses_a_null_command()
    {
        var game = Worlds.Game();
        var player = game.CreatePlayer();

        Should.Throw<ArgumentNullException>(
            () => new DispatchedEvents(player, null!, game.State(player), []));
    }

    [Fact]
    public void Construction_refuses_a_null_state()
    {
        var game = Worlds.Game();
        var player = game.CreatePlayer();

        Should.Throw<ArgumentNullException>(
            () => new DispatchedEvents(player, new BeginSessionCommand("1.0.0", "content"), null!, []));
    }

    [Fact]
    public void Construction_refuses_a_null_event_list()
    {
        var game = Worlds.Game();
        var player = game.CreatePlayer();

        Should.Throw<ArgumentNullException>(
            () => new DispatchedEvents(
                player, new BeginSessionCommand("1.0.0", "content"), game.State(player), null!));
    }

    [Fact]
    public void Construction_accepts_a_whole_batch()
    {
        // The negative control for the three refusals above.
        var game = Worlds.Game();
        var player = game.CreatePlayer();

        Should.NotThrow(
            () => new DispatchedEvents(
                player, new BeginSessionCommand("1.0.0", "content"), game.State(player), []));
    }
}
