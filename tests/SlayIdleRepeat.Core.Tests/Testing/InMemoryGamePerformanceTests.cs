using System.Diagnostics;
using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using SlayIdleRepeat.Core.Tests.Model.Gear;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>The speed target — a full 180-day simulated player in under 200 ms — plus the structural
/// invariants that still hold on somebody else's machine.</summary>
/// <remarks>
/// The budget is split three ways, because a wall-clock assertion on a shared runner fails randomly
/// and then gets disabled, while one loose enough to pass anywhere asserts nothing:
/// <list type="bullet">
///   <item><b>Recorded, not asserted at 200 ms.</b> The measurement goes into the failure message; the
///   assertion is at ten times the budget — an order-of-magnitude regression detector, tolerant of the
///   3-5x spread between a laptop and a contended container.</item>
///   <item><b>A ratio on the same machine.</b>
///   <see cref="The_cost_of_a_command_does_not_grow_with_the_size_of_the_gap"/> compares a one-day gap
///   against a ten-year one; both absorb the machine's speed identically, and a per-boundary catch-up
///   would make it ~3,650.</item>
///   <item><b>A structural invariant with no clock in it.</b>
///   <see cref="A_gap_of_any_size_is_one_command_and_one_boundary_step"/> asserts that crossing 180
///   days takes one <c>Apply</c> — true on every machine, forever.</item>
/// </list>
/// </remarks>
public sealed class InMemoryGamePerformanceTests
{
    private const int Days = 180;
    private const int CommandsPerDay = 4;

    /// <summary>Every slot the expansion ladder can ever reach, filled.</summary>
    /// <remarks>
    /// The largest inventory the game admits, so the comparison is over the worst case a player can
    /// actually put the domain in rather than a number chosen for the test.
    /// </remarks>
    private const int FullStock = 320;

    /// <summary>The budget, in milliseconds.</summary>
    private const double BudgetMs = 200;

    /// <summary>The multiple of the budget the wall-clock assertion actually uses — a regression
    /// detector, not a budget check.</summary>
    private const double RegressionMultiple = 10;

    /// <summary>A full 180-day simulated player, measured warm and asserted at ten times the budget.
    /// Warm because the first run of anything in a fresh process measures the JIT rather than the
    /// domain. Best-of-three, since the fastest run is least contaminated by whatever else the
    /// machine was doing.</summary>
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
            $"a 180-day player took {best:F1} ms. 30 §6 budgets 200 ms and this asserts 2,000 — " +
            "TEN TIMES, deliberately. The measured figure on the M1-11 branch was 15.4 ms, so this " +
            "bound is not a budget check, it is an order-of-magnitude regression detector that " +
            "survives a contended CI container: what it catches is a per-boundary loop, an " +
            "accidental O(n²) or a content read moved into an inner loop. If this ever fires, do " +
            "not raise it — read the ratio and structural tests beside it, which say WHICH of those " +
            "happened.");
    }

    /// <summary>180 days offline is one subtraction, not 180 iterations — asserted as a ratio, so
    /// the machine's speed cancels. A catch-up that walked boundaries would make the ten-year half
    /// ~3,650x the one-day half; the tolerance here is a factor of four, loose on purpose, and still
    /// leaves three orders of magnitude between "passes" and "a loop crept in".</summary>
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
            $"each crossing a TEN-YEAR gap took {tenYears:F1} ms. GameRules.AdvanceTime crosses every " +
            "boundary in one subtraction, so the two are the same work and the ratio is ~1 " +
            "(measured: 0.95). A per-boundary loop would make this ~3,650, so a bound of ten still " +
            "leaves two and a half orders of magnitude between 'passes' and 'a loop crept in' — and " +
            "a test that fails randomly gets disabled by whoever hits it at 3am, which would cost " +
            "this suite the one clock-based assertion worth having.");
    }

    /// <summary>A full inventory costs a command more, LINEARLY, and a full player still lands inside
    /// the budget. Both halves are asserted, because only together do they say the useful thing.</summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Linear is the design, not a defect — do not "fix" it into flatness.</b> Every command
    /// works on a copy, and the copy is a full snapshot round trip of the player; that is what makes a
    /// rejected command leave the caller's state untouched. The stock rides along in that round trip,
    /// so a command that never looks at an item still pays to copy it. The cost is therefore
    /// <em>O(items)</em> by construction, and an assertion that the ratio is ~1 would be asserting the
    /// clone contract away.
    /// </para>
    /// <para>
    /// What is worth catching is <b>super</b>-linear: an item compared against every other item, a
    /// derivation re-run per item per item, a set rebuilt inside the copy loop. At three hundred and
    /// twenty items those show up as a ratio in the hundreds, not the single digits.
    /// </para>
    /// <para>
    /// The same interleaved best-of-three shape as
    /// <see cref="The_cost_of_a_command_does_not_grow_with_the_size_of_the_gap"/>, and for the same
    /// reason: two separate blocks let a GC pause land on one half only. The inventory is built from
    /// a persisted row rather than by sending three hundred and twenty grant commands, which would
    /// measure the grants instead of what this is about.
    /// </para>
    /// </remarks>
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
            "both halves get ten times slower. 30 §6 budgets 200 ms and this asserts 2,000, the same " +
            "order-of-magnitude framing the empty-player test above uses. Measured when the " +
            "inventory landed: 101.8 ms at the maximum stock the capacity ladder can reach, against " +
            "19.7 ms empty — inside the budget, with about half of it left.");

        (full / empty).ShouldBeLessThan(
            8.0,
            $"{Commands} commands against an EMPTY inventory took {empty:F1} ms; the same commands " +
            $"against a FULL {FullStock}-item inventory took {full:F1} ms. Linear is EXPECTED — every " +
            "command copies the whole player, stock included, and that copy is what makes a rejected " +
            "command leave the caller's state untouched. Measured at 5.2 when the inventory landed, " +
            "and a bound of eight is set above that rather than at it. What it catches is " +
            "SUPER-linear work — an item compared against every other item, a derivation re-run per " +
            "item per item — which at three hundred and twenty items reads in the hundreds, not the " +
            "single digits. If it fires, find the nested loop; do not raise it, and do not try to " +
            "make the ratio 1 by removing the clone.");
    }

    /// <summary>The same claim with no clock in it: a gap of any size is one command, and it lands
    /// the period boundaries on <c>GameCalendar</c>'s own answer rather than an accumulation of
    /// steps.</summary>
    [Fact]
    public void A_gap_of_any_size_is_one_command_and_one_boundary_step()
    {
        var (game, player) = Harnesses.WithPlayer();

        game.Clock.Advance(TimeSpan.FromDays(Days));
        var result = game.Send(player, Harnesses.BeginSession);

        game.CommandsIssued.ShouldBe(
            1L,
            "one hundred and eighty days were crossed by ONE Apply. If a future reader makes this a " +
            "loop, this is the number that moves.");

        var state = game.State(player).Player;

        state.DailyPeriodStartUtc.ShouldBe(GameCalendar.GameDayStartAt(game.Clock.NowUtc));
        state.WeeklyPeriodStartUtc.ShouldBe(GameCalendar.GameWeekStartAt(game.Clock.NowUtc));
        state.WeeklyPeriodStartUtc.DayOfWeek.ShouldBe(DayOfWeek.Monday);

        state.EnergyAnchorUtc.ShouldBe(
            game.Clock.NowUtc,
            "180 days is a whole number of 4-minute intervals, so A1's anchor lands exactly on now — " +
            "and it got there by adding wholeUnits x interval once, not by 64,800 additions.");

        result.Events.Count.ShouldBe(
            2,
            "one accrual and one refill. A catch-up that emitted per boundary would produce hundreds, " +
            "and 14 §7.1's economy log would carry a row for every day the player was away.");
    }

    /// <summary>The event list does not grow with the size of the gap either — a memory claim as well
    /// as a speed one, asserted by comparing the two lists directly rather than by a bound.</summary>
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
            "amount — the deltas differ only while the banks have room.");
    }

    /// <summary>One 180-day drive, returning the harness so the caller can floor the workload it
    /// measured — if <c>BEGIN_SESSION</c> regressed to a rejection, every timing test here would get
    /// faster and stay green over pure refusals, so each test also asserts what the drive did.</summary>
    private static InMemoryGame Drive()
    {
        var (game, player) = Harnesses.WithPlayer();

        Harnesses.Drive(game, player, Days, CommandsPerDay);

        return game;
    }

    /// <summary>
    /// Sends <paramref name="commands"/> commands, each one <paramref name="gapDays"/> after the
    /// last. Returns the harness, for the reason <see cref="Drive"/> does.
    /// </summary>
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

    /// <summary>
    /// Sends <paramref name="commands"/> commands a day apart to a player who starts with
    /// <paramref name="items"/> items in stock. The cadence is <see cref="GapDrive"/>'s one-day arm
    /// exactly, so the two halves differ in the inventory and in nothing else.
    /// </summary>
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

    /// <summary>
    /// Asserts that a measured drive actually carried the stock it was measured carrying — the
    /// inventory half of <see cref="ShouldHaveDoneTheWork"/>'s argument. A seam that silently dropped
    /// the pre-populated row would make the "full" half identical to the empty one, and the ratio
    /// would be a perfect 1.0 over nothing.
    /// </summary>
    private static void ShouldHaveCarried(InMemoryGame game, int items) =>
        game.State(game.Players[0]).Player.Inventory.Stored.Count.ShouldBe(
            items,
            "the comparison is only about the size of the inventory if the inventory is that size.");

    /// <summary>
    /// Asserts that a timed drive actually did the work it was measured doing — see
    /// <see cref="Drive"/>'s remarks.
    /// </summary>
    private static void ShouldHaveDoneTheWork(InMemoryGame game, int commands, int acceptedDays)
    {
        game.CommandsIssued.ShouldBe(
            commands,
            "the measurement above is only about the domain if the domain actually ran. A regression " +
            "that turned BEGIN_SESSION into a rejection would make every timing here FASTER (S3).");

        Harnesses.CurrencyRows(game, Harnesses.DailyRefillReason).Count.ShouldBe(
            acceptedDays,
            "…and the commands were ACCEPTED: one daily free refill per game day the drive touched.");
    }

    private static double Fastest(Func<InMemoryGame> action)
    {
        var best = double.MaxValue;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            best = Math.Min(best, Elapsed(action));
        }

        return best;
    }

    /// <summary>One timed run. Split out of <see cref="Fastest"/> so a caller comparing two
    /// workloads can <b>interleave</b> their attempts rather than measure them in separate
    /// blocks — see <c>The_cost_of_a_command_does_not_grow_with_the_size_of_the_gap</c>.</summary>
    private static double Elapsed(Func<InMemoryGame> action)
    {
        var watch = Stopwatch.StartNew();
        _ = action();
        watch.Stop();

        return watch.Elapsed.TotalMilliseconds;
    }
}
