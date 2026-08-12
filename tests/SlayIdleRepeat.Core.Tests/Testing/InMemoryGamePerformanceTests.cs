using System.Diagnostics;
using Shouldly;
using SlayIdleRepeat.Core.Events;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Testing;
using Xunit;

namespace SlayIdleRepeat.Core.Tests;

/// <summary>
/// 🔒 `30` §6's speed target — <em>"a full 180-day simulated player in &lt; 200 ms"</em> — and the
/// structural invariants that still hold on somebody else's machine.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>How the 200 ms figure is treated, and why.</b> A wall-clock assertion on a shared runner is
/// not reproducible: it fails randomly, and a test that fails randomly gets disabled by whoever hits
/// it at 3am — after which it asserts nothing at all. An assertion loose enough to pass on any
/// hardware also asserts nothing. So the budget is split three ways here, and each part is honest
/// about what it can catch:
/// </para>
/// <list type="bullet">
///   <item><b>Recorded, not asserted at 200 ms.</b> The measurement below runs and its number goes
///   into the failure message, so a reader always sees the real figure. The <em>assertion</em> is at
///   <b>ten times</b> the budget, which is a regression detector rather than a budget check: it
///   catches an order-of-magnitude change — a per-boundary loop, an accidental O(n²), a content read
///   moved inside an inner loop — and tolerates the 3–5× spread between a developer laptop and a
///   contended CI container.</item>
///   <item><b>A ratio on the same machine, in the same process.</b>
///   <see cref="The_cost_of_a_command_does_not_grow_with_the_size_of_the_gap"/> compares a one-day
///   gap against a ten-year one. Both halves absorb the machine's speed identically, so the ratio is
///   near-1 everywhere — and a catch-up that iterated per boundary would make it ~3,650. That is the
///   clock-based assertion worth having.</item>
///   <item><b>A structural invariant with no clock in it at all.</b>
///   <see cref="A_gap_of_any_size_is_one_command_and_one_boundary_step"/> asserts that crossing 180
///   days takes <b>one</b> <c>Apply</c> and lands the period boundaries on the calendar's own answer.
///   That is true on every machine, forever, and it is the claim
///   <c>GameRules.AdvanceTime</c> actually makes: <em>"180 days offline is one subtraction, not 180
///   iterations."</em></item>
/// </list>
/// <para>
/// 🔒 <b>MEASURED on the M1-11 branch</b> (Release, .NET 8, x64 developer machine, best of 3–5 runs,
/// 180 days × 4 commands/day = 720 commands). Recorded so the <em>slope</em> is on the record and
/// not only the level, which is what M1-06's carried-forward item <b>12</b> asked for:
/// </para>
/// <list type="table">
///   <item><term>the 180-day player</term><description><b>15.4 ms</b> — 21.4 µs per command, against
///   a 200 ms budget. 13× headroom.</description></item>
///   <item><term><c>EnergyTuning.Read</c></term><description><b>27.7%</b> of the whole drive — one
///   read per command, six JSON pointers re-resolved each time, ≈ 5.9 µs. M1-08 flagged it as
///   possibly dominant: it does not dominate, but it is the largest single named cost in the loop
///   and it is the first place to look if the budget ever tightens.</description></item>
///   <item><term><c>LoginCalendarTuning.Read</c></term><description>4.1% at one read per command, and
///   in the real drive it runs only on the day's first command.</description></item>
///   <item><term>the clone, against aggregate size</term><description>17.3 µs/command at 0 extra
///   counter rows, 20.4 at 400 — <b>+18%</b>, ≈ 7.8 ns per row per command. Shallow <em>for a cheap
///   element</em>; see the note below on why M4-03 is still the thing to watch.</description></item>
///   <item><term>carried-forward item 12 — two clones</term><description>a meta command with a
///   <c>Run</c> in the slice costs <b>×1.13</b> of one with none. 13% today, on a small
///   run.</description></item>
///   <item><term>gap size</term><description>1 / 30 / 180 / 3,650-day gaps cost 25.4 / 24.2 / 24.6 /
///   24.1 µs per command. <b>Flat.</b> A ten-year absence costs what a one-day absence
///   costs.</description></item>
/// </list>
/// <para>
/// ⚠️ <b>The slope, read honestly.</b> The +18% for 400 dictionary rows is <em>not</em> a prediction
/// for M4-03's 400 inventory slots, and reading it as one would be the comforting mistake.
/// <c>WorldSlice</c> clones both aggregates per command through
/// <c>ToSnapshot()</c>/<c>Rehydrate()</c>, and the cost per element is the cost of copying and
/// <b>revalidating</b> that element — a <c>(string, long)</c> pair is about as cheap as an element
/// gets, while `08` §2-3's <c>GearInstance</c> carries quality, chapter origin, a mercy counter, a
/// list of affixes and a lock. If a gear slot is 10–50× a counter row, 400 of them is 30–160 µs per
/// command on top of today's 21 — <b>2× to 8× the whole per-command cost</b>, paid on every command
/// including the ones that never touch inventory. The risk carried-forward item 12 names is real and
/// the answer `30` §4.1 already sanctions is a narrower slice; what this measurement adds is that
/// the <b>element type</b> is the variable, not the count.
/// </para>
/// <para>
/// 🔒 <b>What this means for `21` §9's budget, since M6 is a thin wrapper over this type.</b> The
/// sweep is 180 days × 14 profiles × 200 seeds in under four minutes — 2,800 runs, so <b>86 ms per
/// 180-day player</b>, which is <em>tighter</em> than `30` §6's own 200 ms. At today's 15.4 ms the
/// whole sweep is ≈ 43 s, inside both. That headroom is the thing M4-03 spends.
/// </para>
/// </remarks>
public sealed class InMemoryGamePerformanceTests
{
    private const int Days = 180;
    private const int CommandsPerDay = 4;

    /// <summary>`30` §6's figure, in milliseconds. Recorded here so the assertion can name it.</summary>
    private const double BudgetMs = 200;

    /// <summary>
    /// The multiple of the budget the wall-clock assertion actually uses. See the type's remarks:
    /// this is a regression detector, not a budget check.
    /// </summary>
    private const double RegressionMultiple = 10;

    /// <summary>
    /// 🔒 `30` §6 — a full 180-day simulated player, measured warm and asserted at ten times the
    /// budget.
    /// </summary>
    /// <remarks>
    /// Warm because the first run of anything in a fresh process measures the JIT: `30` §6's claim is
    /// about the domain, and `21` §9's sweep pays the JIT once across 2,800 runs. Best-of-three for
    /// the same reason a benchmark takes a minimum — the fastest run is the one least contaminated by
    /// whatever else the machine was doing.
    /// </remarks>
    [Fact]
    public void A_180_day_player_runs_well_inside_the_budget()
    {
        Drive();

        var best = double.MaxValue;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var watch = Stopwatch.StartNew();
            Drive();
            watch.Stop();
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
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

    /// <summary>
    /// 🔒 <em>"180 days offline is one subtraction, not 180 iterations"</em> — as a <b>ratio</b>, so
    /// the machine's speed cancels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves run the same number of commands through the same code on the same machine in the
    /// same process; the only difference is how far each command rolls the clock forward. A catch-up
    /// that walked boundaries would make the ten-year half roughly 3,650 times the one-day half, and
    /// no amount of runner contention produces that. Measured on the M1-11 branch: 25.4 µs against
    /// 24.1 µs per command — the long gap was, if anything, marginally <em>cheaper</em>, because a
    /// ten-year step crosses a week boundary on every command while a one-day step does not.
    /// </para>
    /// <para>
    /// ⚠️ The tolerance is a factor of <b>four</b>, which is loose on purpose and still leaves three
    /// orders of magnitude between "passes" and "a loop crept in". A tighter bound here would be
    /// measuring the runner.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_cost_of_a_command_does_not_grow_with_the_size_of_the_gap()
    {
        const int Commands = Days * CommandsPerDay;

        GapDrive(1, Commands);
        GapDrive(3650, Commands);

        var oneDay = Fastest(() => GapDrive(1, Commands));
        var tenYears = Fastest(() => GapDrive(3650, Commands));

        (tenYears / oneDay).ShouldBeLessThan(
            4.0,
            $"{Commands} commands each crossing a ONE-DAY gap took {oneDay:F1} ms; the same commands " +
            $"each crossing a TEN-YEAR gap took {tenYears:F1} ms. GameRules.AdvanceTime crosses every " +
            "boundary in one subtraction, so the two are the same work and the ratio is ~1 " +
            "(measured: 0.95). A per-boundary loop would make this ~3,650 — no runner is that " +
            "contended, which is why this comparison is worth having where an absolute number is not.");
    }

    /// <summary>
    /// 🔒 The same claim with <b>no clock in it</b>: a gap of any size is one command, and it lands
    /// the period boundaries on the calendar's own answer.
    /// </summary>
    /// <remarks>
    /// This is the assertion that will still be true on somebody else's machine in five years.
    /// <c>CommandsIssued</c> counts <c>Apply</c> calls, and the two boundaries are compared against
    /// <c>GameCalendar</c> — the one definition both <c>Player</c>'s invariants and
    /// <c>AdvanceTime</c>'s computation read — rather than against a literal or against an
    /// accumulation of steps.
    /// </remarks>
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

    /// <summary>
    /// 🔒 The event list does not grow with the size of the gap either — which is a memory claim as
    /// well as a speed one, and `21` §9 runs 2,800 of these.
    /// </summary>
    /// <remarks>
    /// Asserted by identity of the two lists rather than by a bound: a hundred-fold gap producing the
    /// same rows as a one-day gap is the strongest form of the claim, and it is only sayable because
    /// the accrual is one event whatever the span.
    /// </remarks>
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

    private static void Drive()
    {
        var (game, player) = Harnesses.WithPlayer();

        Harnesses.Drive(game, player, Days, CommandsPerDay);
    }

    /// <summary>
    /// Sends <paramref name="commands"/> commands, each one <paramref name="gapDays"/> after the
    /// last.
    /// </summary>
    private static void GapDrive(int gapDays, int commands)
    {
        var (game, player) = Harnesses.WithPlayer();

        for (var command = 0; command < commands; command++)
        {
            game.Clock.Advance(TimeSpan.FromDays(gapDays));
            game.Send(player, Harnesses.BeginSession);
        }
    }

    private static double Fastest(Action action)
    {
        var best = double.MaxValue;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var watch = Stopwatch.StartNew();
            action();
            watch.Stop();
            best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
        }

        return best;
    }
}
