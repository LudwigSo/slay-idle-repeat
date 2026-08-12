using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Rules.Economy;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Economy;

/// <summary>
/// 🔒 <b>Recorded assumption A1</b> — the accrual rule `10` §3 does not state, and the bug it
/// prevents.
/// </summary>
/// <remarks>
/// <para>
/// `10` §3 gives "1 per 4 minutes" and specifies no rounding. The obvious implementation floors
/// <c>elapsed / 4 min</c> on every call and then moves the accrual anchor to <em>now</em>. That
/// discards the remainder once per call, so a player who sends a hundred commands in an hour
/// accrues far <b>less</b> Energy than one who sends a single command — a frequency-dependent
/// economy bug that silently punishes the most engaged players, and one that no test advancing the
/// clock a single time can see.
/// </para>
/// <para>
/// The rule: accrue <b>whole units only</b>, and advance the anchor by <c>wholeUnits × interval</c>,
/// <b>never</b> to now. The remainder stays banked in the gap between the anchor and now. The
/// property below is what pins it — for randomised splits of one interval into N sub-intervals, N
/// successive accruals must equal one accrual over the whole interval, exactly, for every split.
/// </para>
/// <para>
/// <c>System.Random</c> is banned in <c>Core</c> and <c>Application</c> (`14` §8.1) and is scanned
/// for in those two assemblies only. Here it is seeded with a constant, so the case is reproducible
/// and is the same case on every machine and every run.
/// </para>
/// </remarks>
public sealed class EnergyAccrualPropertyTests
{
    /// <summary>Fixed, so a failure reproduces exactly. Bump it only to widen coverage deliberately.</summary>
    private const int Seed = 20_260_812;

    /// <summary>How many randomised splits to check.</summary>
    private const int Cases = 2_000;

    /// <summary>How many offending splits to quote before the message stops being readable.</summary>
    private const int QuotedFailures = 5;

    private static readonly EnergyTuning Shipped = EnergyTuning.Read(ProgressionDocuments.Shipped);

    /// <summary>
    /// 🔒 <b>The highest-value case in M1-10.</b> N successive accruals over sub-intervals that sum
    /// to T accrue exactly what one accrual over T accrues, and leave the anchor in exactly the same
    /// place — for every randomised split, of every randomised interval, from every randomised
    /// starting state.
    /// </summary>
    [Fact]
    public void Splitting_an_interval_never_changes_what_it_accrues()
    {
        var random = new Random(Seed);
        var quoted = new List<string>();
        var failures = 0;
        var splitsThatAccruedSomething = 0;

        for (var i = 0; i < Cases; i++)
        {
            var legendLevel = random.Next(0, 61);
            var max = EnergyMath.MaxEnergy(Shipped, legendLevel);
            var reserveCapacity = EnergyMath.ReserveCapacity(Shipped, legendLevel);
            var start = new EnergyBanks(random.Next(0, max + 1), random.Next(0, reserveCapacity + 1));

            var total = TimeSpan.FromTicks(random.NextInt64(0, TimeSpan.FromDays(40).Ticks));
            var cuts = Cuts(random, total);

            var whole = EnergyMath.Accrue(Shipped, legendLevel, start, total);

            var banks = start;
            var anchor = TimeSpan.Zero;
            foreach (var cut in cuts)
            {
                var step = EnergyMath.Accrue(Shipped, legendLevel, banks, cut - anchor);
                banks = step.Banks;
                anchor += step.AnchorAdvance;
            }

            if (whole.Banks.Energy > start.Energy || whole.Banks.Reserve > start.Reserve)
            {
                splitsThatAccruedSomething++;
            }

            if (banks == whole.Banks && anchor == whole.AnchorAdvance)
            {
                continue;
            }

            failures++;
            if (quoted.Count < QuotedFailures)
            {
                quoted.Add(Describe(legendLevel, start, total, cuts, banks, anchor, whole));
            }
        }

        // 🔒 S3 — without this the case passes just as happily over 2,000 intervals that all
        // rounded to zero units, which is a property nobody needs.
        splitsThatAccruedSomething.ShouldBeGreaterThan(
            Cases / 2,
            $"only {splitsThatAccruedSomething} of {Cases} randomised intervals accrued any Energy at " +
            "all, so this property is mostly comparing nothing against nothing. The interval range or " +
            "the starting states have drifted.");

        failures.ShouldBe(
            0,
            $"{failures} of {Cases} randomised splits disagreed with the single accrual over the same " +
            "span. N successive accruals must equal one accrual over that span, exactly. If the " +
            "anchor is advanced to 'now' rather than by wholeUnits × the regeneration interval, the " +
            "remainder is discarded once per call and a player who plays actively regenerates less " +
            "than one who does not (A1). First offenders:" + Environment.NewLine +
            string.Join(Environment.NewLine, quoted.Select(q => "  " + q)));
    }

    /// <summary>
    /// 🔒 The same defect as a single, seed-free case: `10` §3's hour of regeneration is 15 Energy
    /// whether the player sent one command in that hour or a hundred.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(15)]
    [InlineData(60)]
    [InlineData(100)]
    public void An_hour_regenerates_fifteen_energy_however_many_commands_it_is_split_into(int commands)
    {
        var hour = TimeSpan.FromHours(1);
        var banks = new EnergyBanks(0, 0);
        var anchor = TimeSpan.Zero;

        for (var command = 1; command <= commands; command++)
        {
            var now = TimeSpan.FromTicks(hour.Ticks * command / commands);
            var step = EnergyMath.Accrue(Shipped, legendLevel: 0, banks, now - anchor);
            banks = step.Banks;
            anchor += step.AnchorAdvance;
        }

        banks.Energy.ShouldBe(
            15,
            $"one hour is 15 Energy at one per four minutes (10 §3). Split into {commands} command(s) " +
            "it came out different, which means the remainder is being discarded per call.");
        banks.Reserve.ShouldBe(0);
        anchor.ShouldBe(hour);
    }

    /// <summary>
    /// The remainder is banked in the gap between the anchor and now, and the very next call sees
    /// it: three minutes accrue nothing and move nothing, and one more minute accrues the point.
    /// </summary>
    [Fact]
    public void The_remainder_survives_in_the_gap_between_the_anchor_and_now()
    {
        var first = EnergyMath.Accrue(
            Shipped, legendLevel: 0, new EnergyBanks(0, 0), TimeSpan.FromMinutes(3));

        first.Banks.Energy.ShouldBe(0);
        first.AnchorAdvance.ShouldBe(
            TimeSpan.Zero,
            "the anchor must not move for a partial unit — the three minutes are the remainder, and " +
            "moving the anchor over them is exactly how they get discarded.");

        // The anchor did not move, so one minute later the gap is four minutes, not one.
        var second = EnergyMath.Accrue(
            Shipped, legendLevel: 0, first.Banks, TimeSpan.FromMinutes(4));

        second.Banks.Energy.ShouldBe(1);
        second.AnchorAdvance.ShouldBe(TimeSpan.FromMinutes(4));
    }

    /// <summary>
    /// 🔒 The anchor is only ever advanced by whole regeneration intervals, and never past the
    /// instant it was asked about. Both halves matter: the first is A1, the second stops the anchor
    /// running into the future and freezing regeneration.
    /// </summary>
    [Fact]
    public void The_anchor_advances_by_whole_intervals_and_never_past_now()
    {
        var random = new Random(Seed);
        var offenders = new List<string>();

        for (var i = 0; i < Cases; i++)
        {
            var legendLevel = random.Next(0, 61);
            var elapsed = TimeSpan.FromTicks(random.NextInt64(0, TimeSpan.FromDays(40).Ticks));
            var advance = EnergyMath
                .Accrue(Shipped, legendLevel, new EnergyBanks(0, 0), elapsed)
                .AnchorAdvance;

            if (advance > elapsed)
            {
                offenders.Add($"{Render(elapsed)} advanced the anchor by {Render(advance)} — into the future");
            }
            else if (advance.Ticks % Shipped.RegenInterval.Ticks != 0)
            {
                offenders.Add($"{Render(elapsed)} advanced the anchor by {Render(advance)}, not a whole " +
                              $"multiple of {Render(Shipped.RegenInterval)}");
            }
            else if (elapsed - advance >= Shipped.RegenInterval)
            {
                offenders.Add($"{Render(elapsed)} advanced the anchor by only {Render(advance)}, leaving " +
                              $"{Render(elapsed - advance)} unconsumed — a whole unit was left behind");
            }
        }

        offenders.Take(QuotedFailures).ShouldBeEmpty(
            "the anchor advance is wholeUnits × the regeneration interval: never past now, never a " +
            "fraction of an interval, and never short by a whole one (A1).");
    }

    private static TimeSpan[] Cuts(Random random, TimeSpan total)
    {
        var count = random.Next(2, 13);
        var cuts = new TimeSpan[count];

        for (var i = 0; i < count - 1; i++)
        {
            cuts[i] = TimeSpan.FromTicks(total.Ticks == 0 ? 0 : random.NextInt64(0, total.Ticks + 1));
        }

        cuts[count - 1] = total;
        Array.Sort(cuts);

        return cuts;
    }

    private static string Describe(
        int legendLevel,
        EnergyBanks start,
        TimeSpan total,
        TimeSpan[] cuts,
        EnergyBanks split,
        TimeSpan splitAnchor,
        EnergyAccrual whole) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"level {legendLevel} from ({start.Energy}, {start.Reserve}) over {Render(total)} " +
            $"cut at [{string.Join(", ", cuts.Select(Render))}]: " +
            $"split gives ({split.Energy}, {split.Reserve}) anchor {Render(splitAnchor)}, " +
            $"whole gives ({whole.Banks.Energy}, {whole.Banks.Reserve}) anchor {Render(whole.AnchorAdvance)}");

    private static string Render(TimeSpan span) =>
        span.ToString("c", CultureInfo.InvariantCulture);
}
