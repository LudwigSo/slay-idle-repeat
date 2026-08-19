using System.Diagnostics;
using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>30 §6's speed target, split three ways: a wall-clock bound at ten times the budget (an
/// order-of-magnitude regression detector that survives a contended runner), a same-machine ratio
/// (the machine's speed cancels), and structural invariants with no clock in them.</summary>
public sealed class InMemoryGamePerformanceTests
{
    private const int Days = 180;
    private const int CommandsPerDay = 4;

    /// <summary>Read off the tuning rather than restated, so the fixture cannot drift from the
    /// flat capacity ceiling it claims to be measuring.</summary>
    private static readonly int FullStock = Inventories.Tuning.MaxCapacity;

    /// <summary>30 §6's budget, in milliseconds.</summary>
    private const double BudgetMs = 200;

    /// <summary>The multiple of the budget the wall-clock assertion actually uses — a regression
    /// detector, not a budget check.</summary>
    private const double RegressionMultiple = 10;

    /// <summary>Warm because a cold first run measures the JIT; best-of-three because the fastest
    /// run is least contaminated by whatever else the machine was doing.</summary>
    [Fact]
    public void A_180_day_player_runs_well_inside_the_budget()
    {
        ShouldHaveDoneTheWork(Drive(), Days * CommandsPerDay, Days);

        var best = double.MaxValue;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var watch = Stopwatch.StartNew();
            var game = Drive();
            watch.Stop();
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);

            ShouldHaveDoneTheWork(game, Days * CommandsPerDay, Days);
        }

        best.ShouldBeLessThan(
            BudgetMs * RegressionMultiple,
            $"a 180-day player took {best:F1} ms against 30 §6's 200 ms budget, asserted at ten " +
            "times deliberately: this catches a per-boundary loop, an accidental O(n²) or a content " +
            "read moved into an inner loop. If it fires, do not raise it — the ratio and structural " +
            "tests beside it say which of those happened.");
    }

    /// <summary>A catch-up that walked boundaries would make the ten-year half ~3,650x the one-day
    /// half; the bound of ten is loose on purpose and still leaves orders of magnitude between
    /// "passes" and "a loop crept in".</summary>
    [Fact]
    public void The_cost_of_a_command_does_not_grow_with_the_size_of_the_gap()
    {
        const int Commands = Days * CommandsPerDay;

        ShouldHaveDoneTheWork(GapDrive(1, Commands), Commands, Commands);
        ShouldHaveDoneTheWork(GapDrive(3650, Commands), Commands, Commands);

        // Interleaved rather than two separate best-of-3 blocks, so a GC pause or CPU steal lands on
        // both halves equally rather than skewing just one of them.
        var oneDay = double.MaxValue;
        var tenYears = double.MaxValue;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            oneDay = Math.Min(oneDay, Elapsed(() => GapDrive(1, Commands)));
            tenYears = Math.Min(tenYears, Elapsed(() => GapDrive(3650, Commands)));
        }

        (tenYears / oneDay).ShouldBeLessThan(
            10.0,
            $"{Commands} commands each crossing a ONE-DAY gap took {oneDay:F1} ms; the same commands " +
            $"each crossing a TEN-YEAR gap took {tenYears:F1} ms. GameRules.AdvanceTime crosses " +
            "every boundary in one subtraction, so the ratio is ~1; a per-boundary loop would make " +
            "it ~3,650.");
    }

    /// <summary>Linear is the design, not a defect — every command works on a full snapshot copy of
    /// the player, which is what lets a rejected command leave the caller's state untouched, and the
    /// stock rides along in that copy. What is worth catching is SUPER-linear: an item compared
    /// against every other item shows up as a ratio in the thousands at a full stock, not the low
    /// teens. The bound of 20 sits above the linear prediction (ratio is 1 + k × items) and two
    /// orders of magnitude below an O(n²) clone.</summary>
    [Fact]
    public void A_full_inventory_costs_a_command_only_linearly_and_stays_inside_the_budget()
    {
        const int Commands = Days * CommandsPerDay;

        ShouldHaveDoneTheWork(InventoryDrive(0, Commands), Commands, Commands);
        ShouldHaveDoneTheWork(InventoryDrive(FullStock, Commands), Commands, Commands);

        ShouldHaveCarried(InventoryDrive(0, Commands), 0);
        ShouldHaveCarried(InventoryDrive(FullStock, Commands), FullStock);

        var empty = double.MaxValue;
        var full = double.MaxValue;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            empty = Math.Min(empty, Elapsed(() => InventoryDrive(0, Commands)));
            full = Math.Min(full, Elapsed(() => InventoryDrive(FullStock, Commands)));
        }

        full.ShouldBeLessThan(
            BudgetMs * RegressionMultiple,
            $"a 180-day player carrying a FULL {FullStock}-item inventory took {full:F1} ms. This is " +
            "the half of the budget question a ratio cannot answer: the ratio stays honest even if " +
            "both halves get ten times slower.");

        (full / empty).ShouldBeLessThan(
            20.0,
            $"{Commands} commands against an EMPTY inventory took {empty:F1} ms; the same commands " +
            $"against a FULL {FullStock}-item inventory took {full:F1} ms. Linear is EXPECTED — " +
            "every command copies the whole player, stock included. If this fires, find the nested " +
            "loop; do not raise the bound without re-deriving it from a measured per-item cost, and " +
            "do not try to make the ratio 1 by removing the clone.");
    }

    /// <summary>The same claim with no clock in it.</summary>
    [Fact]
    public void A_gap_of_any_size_is_one_command_and_one_boundary_step()
    {
        var (game, player) = Harnesses.WithPlayer();

        game.Clock.Advance(TimeSpan.FromDays(Days));
        var result = game.Send(player, Harnesses.BeginSession);

        game.CommandsIssued.ShouldBe(1L, "one hundred and eighty days were crossed by ONE Apply.");

        var state = game.State(player).Player;

        state.DailyPeriodStartUtc.ShouldBe(GameCalendar.GameDayStartAt(game.Clock.NowUtc));
        state.WeeklyPeriodStartUtc.ShouldBe(GameCalendar.GameWeekStartAt(game.Clock.NowUtc));
        state.WeeklyPeriodStartUtc.DayOfWeek.ShouldBe(DayOfWeek.Monday);

        state.EnergyAnchorUtc.ShouldBe(
            game.Clock.NowUtc,
            "180 days is a whole number of regen intervals, so A1's anchor lands exactly on now — " +
            "by adding wholeUnits × interval once, not by tens of thousands of additions.");

        result.Events.Count.ShouldBe(
            2,
            "one accrual and one refill. A catch-up that emitted per boundary would put a row in " +
            "14 §7.1's economy log for every day the player was away.");
    }

    /// <summary>A memory claim as well as a speed one, asserted by comparing the two lists directly
    /// rather than by a bound.</summary>
    [Fact]
    public void The_event_list_does_not_grow_with_the_size_of_the_gap()
    {
        var (near, nearPlayer) = Harnesses.WithPlayer();
        near.Clock.Advance(TimeSpan.FromDays(1));
        var nearRows = near.Send(nearPlayer, Harnesses.BeginSession).Events;

        var (far, farPlayer) = Harnesses.WithPlayer();
        far.Clock.Advance(TimeSpan.FromDays(365 * 10));
        var farRows = far.Send(farPlayer, Harnesses.BeginSession).Events;

        farRows.Count.ShouldBe(nearRows.Count);

        farRows.Select(e => ((CurrencyChanged)e).Reason)
            .ShouldBe(nearRows.Select(e => ((CurrencyChanged)e).Reason));

        farRows.OfType<CurrencyChanged>().First().Delta.ShouldBe(
            nearRows.OfType<CurrencyChanged>().First().Delta,
            "both accruals are capped by the tank, so a decade away and a day away deposit the same " +
            "amount.");
    }

    private static InMemoryGame Drive()
    {
        var (game, player) = Harnesses.WithPlayer();

        Harnesses.Drive(game, player, Days, CommandsPerDay);

        return game;
    }

    private static InMemoryGame GapDrive(int gapDays, int commands)
    {
        var (game, player) = Harnesses.WithPlayer();

        for (var command = 0; command < commands; command++)
        {
            game.Clock.Advance(TimeSpan.FromDays(gapDays));
            game.Send(player, Harnesses.BeginSession);
        }

        return game;
    }

    /// <summary><see cref="GapDrive"/>'s one-day arm exactly, so the two halves of the inventory
    /// comparison differ in the stock and in nothing else.</summary>
    private static InMemoryGame InventoryDrive(int items, int commands)
    {
        var (game, player) = Harnesses.WithPlayer(inventory: Inventories.Stock(items));

        for (var command = 0; command < commands; command++)
        {
            game.Clock.Advance(TimeSpan.FromDays(1));
            game.Send(player, Harnesses.BeginSession);
        }

        return game;
    }

    /// <summary>A seam that silently dropped the pre-populated stock would make the "full" half
    /// identical to the empty one, and the ratio a perfect 1.0 over nothing.</summary>
    private static void ShouldHaveCarried(InMemoryGame game, int items) =>
        game.State(game.Players[0]).Player.Inventory.Stored.Count.ShouldBe(items);

    /// <summary>A regression that turned <c>BEGIN_SESSION</c> into a rejection would make every
    /// timing here FASTER and stay green over pure refusals.</summary>
    private static void ShouldHaveDoneTheWork(InMemoryGame game, int commands, int acceptedDays)
    {
        game.CommandsIssued.ShouldBe(commands);

        Harnesses.CurrencyRows(game, Harnesses.DailyRefillReason).Count.ShouldBe(
            acceptedDays, "one daily free refill per game day the drive touched proves the commands " +
            "were accepted.");
    }

    private static double Elapsed(Func<InMemoryGame> action)
    {
        var watch = Stopwatch.StartNew();
        _ = action();
        watch.Stop();

        return watch.Elapsed.TotalMilliseconds;
    }
}
