using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>A fixed seed reproduces byte-for-byte.</summary>
/// <remarks>
/// Compared through <c>CanonicalStateWriter.HashMetaCommandState</c>, not record equality:
/// <c>PlayerSnapshot</c> carries three dictionaries a record compares by reference, so two snapshots
/// describing the identical player would never be equal.
/// <para>
/// Nothing in M1 draws, so the seed's effect on state is zero today — these tests are the tripwire
/// that goes red the day something starts drawing from it.
/// </para>
/// </remarks>
public sealed class InMemoryGameDeterminismTests
{
    private const int Days = 180;
    private const int CommandsPerDay = 4;

    /// <summary>Two fresh harnesses, same seed, same command sequence: identical state and an
    /// identical event list, compared as a sequence rather than a set or a count.</summary>
    [Fact]
    public void Two_fresh_harnesses_with_one_seed_produce_identical_state_and_events()
    {
        var (firstState, firstEvents) = Run(Harnesses.Seed);
        var (secondState, secondEvents) = Run(Harnesses.Seed);

        firstState.ShouldBe(secondState);
        firstEvents.ShouldBe(secondEvents);

        firstEvents.Count.ShouldBeGreaterThan(
            Days,
            "a 180-day drive produces at least one row per game day; if this list were empty the " +
            "comparison above would hold over nothing (S3).");
    }

    /// <summary>The same drive, twice inside one process: nothing static accumulates between runs.
    /// Two harnesses in one process share every static in <c>Core</c> — <c>GameRules</c>' dispatch
    /// table above all — so a mutated table or a cached, player-tainted tuning would make the second
    /// simulation differ from the first.</summary>
    [Fact]
    public void The_same_drive_twice_in_one_process_gives_the_same_answer()
    {
        var first = Run(Harnesses.Seed);
        var second = Run(Harnesses.Seed);
        var third = Run(Harnesses.Seed);

        second.State.ShouldBe(first.State);
        third.State.ShouldBe(first.State);
        second.Events.ShouldBe(first.Events);
        third.Events.ShouldBe(first.Events);
    }

    /// <summary>Two harnesses in flight at once, interleaved, do not contaminate each other — a
    /// case the sequential tests above cannot see, since each runs its simulation to completion
    /// before the next starts.</summary>
    /// <remarks>Does not pin that the per-command seed counter is per player rather than global —
    /// each harness here holds one player, so a global counter would look identical. That only
    /// becomes testable once something actually reads <c>CommandSeed</c>.</remarks>
    [Fact]
    public void Two_interleaved_harnesses_do_not_contaminate_each_other()
    {
        var alone = Run(Harnesses.Seed);

        var left = Harnesses.New();
        var leftPlayer = left.CreatePlayer();
        var right = Harnesses.New(seed: Harnesses.Seed ^ 0xFFFF_FFFF_FFFF_FFFFUL);
        var rightPlayer = right.CreatePlayer();

        for (var day = 0; day < Days; day++)
        {
            Harnesses.DriveDay(left, leftPlayer, CommandsPerDay);
            Harnesses.DriveDay(right, rightPlayer, CommandsPerDay);
        }

        CanonicalStateWriter.HashMetaCommandState(left.State(leftPlayer).Player.ToSnapshot())
            .ShouldBe(
                alone.State,
                "the left harness ran the SAME drive as the sequential one — DriveDay, not a " +
                "hand-written cadence — with another simulation running a day between every one of " +
                "its days.");

        CanonicalStateWriter.HashMetaCommandState(right.State(rightPlayer).Player.ToSnapshot())
            .ShouldBe(alone.State, "…and so did the right one, on a different seed (see below).");

        left.Events.ShouldBe(
            alone.Events,
            "the event lists are per harness: the interleaving must not merge one simulation's rows " +
            "into another's, which a static accumulator would.");
    }

    /// <summary>Two different seeds produce the same state today — not a claim that the seed does
    /// not matter, but that nothing in M1 draws from it yet. This is the tripwire on that deferral:
    /// the commit that takes the first draw should turn this red.</summary>
    [Fact]
    public void A_different_seed_changes_nothing_yet_and_this_test_expires_at_M4_09()
    {
        var first = Run(Harnesses.Seed);
        var second = Run(Harnesses.Seed ^ 0xDEAD_BEEF_CAFE_F00DUL);
        var zero = Run(0UL);

        second.State.ShouldBe(
            first.State,
            "nothing in M1 draws from CommandSeed. When M4-09 takes 19 B's quest slate or 10 §5.1's " +
            "Daily shop block from the day's draw seed, this goes RED — and that is the point: " +
            "replace it then with 'different seeds draw differently, the same seed reproduces', do " +
            "not delete it.");

        zero.State.ShouldBe(
            first.State,
            "…including seed 0, which is a legitimate seed and not an absence (30 §3).");

        second.Events.ShouldBe(first.Events);
    }

    /// <summary>The comparison itself has teeth: a drive that differs by one command produces a
    /// different state hash — otherwise every determinism assertion above could pass against a
    /// constant.</summary>
    [Fact]
    public void The_state_comparison_reports_a_difference_when_there_is_one()
    {
        var full = Run(Harnesses.Seed);
        var shorter = Run(Harnesses.Seed, days: Days - 1);

        shorter.State.ShouldNotBe(full.State);
        shorter.Events.Count.ShouldBeLessThan(full.Events.Count);
    }

    /// <summary>One 180-day drive: the player's state hash and the whole event list.</summary>
    private static (string State, IReadOnlyList<DomainEvent> Events) Run(ulong seed, int days = Days)
    {
        var (game, player) = Harnesses.WithPlayer(seed: seed);

        Harnesses.Drive(game, player, days, CommandsPerDay);

        return (
            CanonicalStateWriter.HashMetaCommandState(game.State(player).Player.ToSnapshot()),
            game.Events.ToArray());
    }
}
