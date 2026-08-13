using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Content;
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
        var discriminating = 0;
        var saturated = 0;

        for (var i = 0; i < Cases; i++)
        {
            var legendLevel = random.Next(1, 61);
            var max = EnergyMath.MaxEnergy(Shipped, legendLevel);
            var reserveCapacity = EnergyMath.ReserveCapacity(Shipped, legendLevel);
            var start = new EnergyBanks(random.Next(0, max + 1), random.Next(0, reserveCapacity + 1));

            var total = SampleSpan(random, start, max, reserveCapacity, saturating: i % 5 == 0);
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

            if (IsDiscriminating(start, whole.Banks, max, reserveCapacity))
            {
                discriminating++;
            }
            else if (whole.Banks.Energy >= max && whole.Banks.Reserve >= reserveCapacity)
            {
                saturated++;
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

        // 🔒 S3 — the floor, and it is not "did anything accrue". A span long enough to fill both
        // banks saturates whatever the split does, and a saturated case cannot tell a correct
        // accrual from one that discards its remainder once per call — it passes either way.
        //
        // Measured, so the floor is set against a number rather than a guess. Drawn uniformly over
        // 40 days, only ~1% of cases were discriminating and the naive anchor advance was caught in
        // 21 of 2,000. Sampling each span against that case's own headroom instead gives 1,573
        // discriminating and 407 saturated, and the same naive implementation now fails 1,487.
        // The floor sits under the measured 79%, not under the old 1%.
        discriminating.ShouldBeGreaterThan(
            Cases * 6 / 10,
            $"only {discriminating} of {Cases} randomised cases accrued something WITHOUT filling " +
            "both banks. The rest are saturated, and a saturated case proves nothing about the " +
            "remainder. The span sampling has drifted.");

        // 🔒 The other half of the sampling, floored for the same reason: SampleSpan's docstring
        // claims one case in five reaches the saturating regime and covers the Reserve cap under
        // splitting. Break that branch and nothing else here would notice.
        saturated.ShouldBeGreaterThan(
            Cases / 10,
            $"only {saturated} of {Cases} randomised cases filled both banks, so the saturating " +
            "branch of SampleSpan has stopped reaching the Reserve cap.");

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
            var step = EnergyMath.Accrue(Shipped, legendLevel: 1, banks, now - anchor);
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
            Shipped, legendLevel: 1, new EnergyBanks(0, 0), TimeSpan.FromMinutes(3));

        first.Banks.Energy.ShouldBe(0);
        first.AnchorAdvance.ShouldBe(
            TimeSpan.Zero,
            "the anchor must not move for a partial unit — the three minutes are the remainder, and " +
            "moving the anchor over them is exactly how they get discarded.");

        // The anchor did not move, so one minute later the gap is four minutes, not one.
        var second = EnergyMath.Accrue(
            Shipped, legendLevel: 1, first.Banks, TimeSpan.FromMinutes(4));

        second.Banks.Energy.ShouldBe(1);
        second.AnchorAdvance.ShouldBe(TimeSpan.FromMinutes(4));
    }

    /// <summary>
    /// 🔒 The anchor is only ever advanced by whole regeneration intervals, and never past the
    /// instant it was asked about. Both halves matter: the first is A1, the second stops the anchor
    /// running into the future and freezing regeneration.
    /// </summary>
    /// <remarks>
    /// ⚠️ This one samples <c>elapsed</c> uniformly over forty days — the very sampling its sibling
    /// was fixed <em>away</em> from, because at 7,200+ units against banks holding 400 the accrual
    /// saturates and both implementations agree (M1-10 measured 21 failures where there were 1,487).
    /// That is sound <b>here</b>, because all three offender branches are about the <b>anchor</b>,
    /// which advances regardless of saturation — but a reader comparing this against the two floored
    /// properties in this file cannot tell that from the code, so the claim is asserted rather than
    /// inferred: nearly every case must actually move the anchor, or the sampling has drifted.
    /// </remarks>
    [Fact]
    public void The_anchor_advances_by_whole_intervals_and_never_past_now()
    {
        var random = new Random(Seed);
        var offenders = new List<string>();
        var meaningful = 0;

        for (var i = 0; i < Cases; i++)
        {
            var legendLevel = random.Next(1, 61);
            var elapsed = TimeSpan.FromTicks(random.NextInt64(0, TimeSpan.FromDays(40).Ticks));
            var advance = EnergyMath
                .Accrue(Shipped, legendLevel, new EnergyBanks(0, 0), elapsed)
                .AnchorAdvance;

            if (advance > TimeSpan.Zero)
            {
                meaningful++;
            }

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

        // 🔒 S3 floor on the sampling itself — the same claim the two floored properties in this file
        // make. A span shorter than one regeneration interval advances the anchor by zero and every
        // branch above is trivially satisfied, so a sampler that drifted short would leave this test
        // green over nothing.
        meaningful.ShouldBeGreaterThan(
            Cases * 9 / 10,
            $"only {meaningful} of {Cases} sampled spans advanced the anchor at all. Every offender " +
            "branch above is vacuous for a zero advance, so the span sampling has drifted short and " +
            "this property is asserting over almost nothing.");
    }

    /// <summary>
    /// A span to accrue over. Four cases in five land in the band where the two banks still have
    /// headroom — the only band in which a discarded remainder is visible at all — and the fifth
    /// runs anywhere up to forty days, so the saturating regime and the Reserve cap are covered too.
    /// </summary>
    private static TimeSpan SampleSpan(
        Random random, EnergyBanks start, int max, int reserveCapacity, bool saturating)
    {
        if (saturating)
        {
            return TimeSpan.FromTicks(random.NextInt64(0, TimeSpan.FromDays(40).Ticks));
        }

        // One unit past the headroom, so the boundary at which the banks fill is itself sampled.
        var headroom = max - start.Energy + reserveCapacity - start.Reserve + 1;

        return TimeSpan.FromTicks(random.NextInt64(0, headroom * Shipped.RegenInterval.Ticks));
    }

    /// <summary>
    /// True when the case can tell a correct accrual from one that discards its remainder: it
    /// accrued something, and it did <em>not</em> end with both banks full — a full pair absorbs
    /// any difference in what was accrued and reports the same answer either way.
    /// </summary>
    private static bool IsDiscriminating(
        EnergyBanks start, EnergyBanks after, int max, int reserveCapacity) =>
        (after.Energy > start.Energy || after.Reserve > start.Reserve) &&
        !(after.Energy >= max && after.Reserve >= reserveCapacity);

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
