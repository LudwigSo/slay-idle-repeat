using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>Compared through <c>CanonicalStateWriter.HashMetaCommandState</c>, not record equality:
/// <c>PlayerSnapshot</c> carries dictionaries a record compares by reference.</summary>
public sealed class InMemoryGameDeterminismTests
{
    private const int Days = 180;
    private const int CommandsPerDay = 4;

    [Fact]
    public void Two_fresh_harnesses_with_one_seed_produce_identical_state_and_events()
    {
        var (firstState, firstEvents) = Run();
        var (secondState, secondEvents) = Run();

        firstState.ShouldBe(secondState);
        firstEvents.ShouldBe(secondEvents);

        firstEvents.Count.ShouldBeGreaterThan(
            Days,
            "a 180-day drive produces at least one row per game day; over an empty list the " +
            "comparison above would hold over nothing.");
    }

    /// <summary>The sequential test cannot see this case: each of its simulations runs to completion
    /// before the next starts.</summary>
    [Fact]
    public void Two_interleaved_harnesses_do_not_contaminate_each_other()
    {
        var alone = Run();

        var left = Harnesses.New();
        var leftPlayer = left.CreatePlayer();
        var right = Harnesses.New();
        var rightPlayer = right.CreatePlayer();

        for (var day = 0; day < Days; day++)
        {
            Harnesses.DriveDay(left, leftPlayer, CommandsPerDay);
            Harnesses.DriveDay(right, rightPlayer, CommandsPerDay);
        }

        CanonicalStateWriter.HashMetaCommandState(left.State(leftPlayer).Player.ToSnapshot())
            .ShouldBe(
                alone.State,
                "the left harness ran the same DriveDay cadence as the sequential run, with another " +
                "simulation running a day between every one of its days.");

        CanonicalStateWriter.HashMetaCommandState(right.State(rightPlayer).Player.ToSnapshot())
            .ShouldBe(alone.State);

        left.Events.ShouldBe(
            alone.Events,
            "the event lists are per harness: the interleaving must not merge one simulation's rows " +
            "into another's, which a static accumulator would.");
    }

    private static (string State, IReadOnlyList<DomainEvent> Events) Run()
    {
        var (game, player) = Harnesses.WithPlayer();

        Harnesses.Drive(game, player, Days, CommandsPerDay);

        return (
            CanonicalStateWriter.HashMetaCommandState(game.State(player).Player.ToSnapshot()),
            game.Events.ToArray());
    }
}
