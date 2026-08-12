using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Rules.Economy;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Economy;

/// <summary>
/// `10` §3 and `28` Part C — the Energy and Energy Reserve math: Max Energy, regeneration,
/// overflow routing, grants, refills and the automatic main-bar-first spend order.
/// </summary>
/// <remarks>
/// The accrual rule itself — whole units only, anchor advanced by <c>units × interval</c> and never
/// to "now" — has its own suite: <see cref="EnergyAccrualPropertyTests"/>. This one covers the
/// arithmetic around it.
/// </remarks>
public sealed class EnergyMathTests
{
    private static readonly EnergyTuning Shipped = EnergyTuning.Read(ProgressionDocuments.Shipped);

    // ------------------------------------------------------------------ Max Energy

    /// <summary>`10` §3 — Max Energy is 120 (+2 per Legend Level, cap 200).</summary>
    /// <remarks>
    /// ⚠️ Note row <c>(1, 122)</c>. `07` §1.1 starts a player at Legend Level <b>1</b>, so under
    /// the formula the M1 kickoff authored — <c>baseMax + perLegendLevel × legendLevel</c> — the
    /// base 120 belongs to Legend Level 0, a level no player occupies. See
    /// <c>EnergyMath.MaxEnergy</c>'s remarks: it is a live contradiction with three other `10` §3
    /// numbers, implemented as ruled and registered rather than silently renumbered here.
    /// </remarks>
    [Theory]
    [InlineData(0, 120)]
    [InlineData(1, 122)]
    [InlineData(10, 140)]
    [InlineData(39, 198)]
    [InlineData(40, 200)]
    [InlineData(41, 200)]
    [InlineData(200, 200)]
    public void Max_energy_is_120_plus_2_per_legend_level_capped_at_200(int legendLevel, int expected) =>
        EnergyMath.MaxEnergy(Shipped, legendLevel).ShouldBe(expected);

    /// <summary>
    /// 🔒 `28` C2 — the Reserve holds <b>1× Max Energy</b>, not a hard-coded 200. At a low Legend
    /// Level the two are 120, and a reader that had baked in the cap would say 200 here.
    /// </summary>
    [Theory]
    [InlineData(0, 120)]
    [InlineData(1, 122)]
    [InlineData(39, 198)]
    [InlineData(40, 200)]
    [InlineData(200, 200)]
    public void Reserve_capacity_tracks_current_max_energy_not_the_cap(int legendLevel, int expected)
    {
        EnergyMath.ReserveCapacity(Shipped, legendLevel).ShouldBe(expected);
        EnergyMath.ReserveCapacity(Shipped, legendLevel)
            .ShouldBe(EnergyMath.MaxEnergy(Shipped, legendLevel));
    }

    /// <summary>`28` C2's 📐 dial: raise the multiple and the Reserve grows with it.</summary>
    [Fact]
    public void Raising_the_reserve_multiple_raises_the_reserve_capacity()
    {
        var doubled = EnergyTuning.Read(
            ProgressionDocuments.With(reserveMultipleOfMax: ContentValue.Number(2)));

        EnergyMath.ReserveCapacity(doubled, legendLevel: 40).ShouldBe(400);
        EnergyMath.MaxEnergy(doubled, legendLevel: 40).ShouldBe(200);
    }

    /// <summary>
    /// 🔒 Every entry point guards the Legend Level, not just the one that uses it directly.
    /// <c>Accrue</c> and <c>Grant</c> call <c>RequireLegendLevel</c> themselves; without a case per
    /// entry point, deleting either call changes nothing observable (the nested <c>MaxEnergy</c>
    /// still throws) and no test notices the guard has gone.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryEntryPoint))]
    public void A_negative_legend_level_is_refused_by_every_entry_point(
        string name, Action<int> call)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => call(-1));

        thrown.ParamName.ShouldBe("legendLevel", $"{name} did not name the offending parameter.");
    }

    /// <summary>
    /// Every entry point refuses a null tuning rather than dereferencing it. A <c>[Fact]</c> over a
    /// local list rather than a <c>[Theory]</c>: <c>EnergyTuning</c> is <c>internal</c>, so an
    /// <c>Action&lt;EnergyTuning&gt;</c> cannot appear on a public <c>MemberData</c> property.
    /// </summary>
    [Fact]
    public void A_null_tuning_is_refused_by_every_entry_point()
    {
        var entryPoints = new (string Name, Action Call)[]
        {
            ("MaxEnergy", () => EnergyMath.MaxEnergy(null!, 40)),
            ("ReserveCapacity", () => EnergyMath.ReserveCapacity(null!, 40)),
            ("Accrue", () => EnergyMath.Accrue(null!, 40, default, TimeSpan.FromHours(1))),
            ("Grant", () => EnergyMath.Grant(null!, 40, default, 10)),
            ("RefillToFull", () => EnergyMath.RefillToFull(null!, 40, default)),
        };

        entryPoints.Length.ShouldBe(
            5, "EnergyMath has an entry point taking a tuning that this case does not cover.");

        foreach (var (name, call) in entryPoints)
        {
            var thrown = Should.Throw<ArgumentNullException>(call);

            thrown.ParamName.ShouldBe("tuning", $"{name} did not name the offending parameter.");
        }
    }

    /// <summary>The four entry points that take a Legend Level, as callables.</summary>
    public static TheoryData<string, Action<int>> EveryEntryPoint => new()
    {
        { "MaxEnergy", level => EnergyMath.MaxEnergy(Shipped, level) },
        { "ReserveCapacity", level => EnergyMath.ReserveCapacity(Shipped, level) },
        { "Accrue", level => EnergyMath.Accrue(Shipped, level, default, TimeSpan.FromHours(1)) },
        { "Grant", level => EnergyMath.Grant(Shipped, level, default, 10) },
        { "RefillToFull", level => EnergyMath.RefillToFull(Shipped, level, default) },
    };

    /// <summary>
    /// 🔒 `21` §12 calls every economy number a placeholder and the simulator sweeps them. The
    /// 64-bit widening in <c>MaxEnergy</c> and the <c>int.MaxValue</c> clamp in
    /// <c>ReserveCapacity</c> both carry a written rationale and neither was reachable from this
    /// suite — reverting either to 32-bit arithmetic left everything green.
    /// </summary>
    [Fact]
    public void A_swept_per_level_increment_cannot_wrap_max_energy_negative()
    {
        var swept = EnergyTuning.Read(
            ProgressionDocuments.With(perLegendLevel: ContentValue.Number(2_000_000_000)));

        // In 32-bit, 2e9 * 2 wraps to about -295 million, which then wins Math.Min against the cap.
        EnergyMath.MaxEnergy(swept, legendLevel: 2).ShouldBe(200);
        EnergyMath.MaxEnergy(swept, legendLevel: 200).ShouldBe(200);
    }

    /// <summary>🔒 The companion clamp: `28` C2's dial cannot multiply the Reserve past an int.</summary>
    [Fact]
    public void A_swept_reserve_multiple_clamps_rather_than_wrapping()
    {
        var swept = EnergyTuning.Read(
            ProgressionDocuments.With(reserveMultipleOfMax: ContentValue.Number(2_000_000_000)));

        EnergyMath.ReserveCapacity(swept, legendLevel: 40).ShouldBe(int.MaxValue);
    }

    // ------------------------------------------------------------------ accrual arithmetic

    /// <summary>`10` §3 — "Full refill time: 8 hours from empty", at the base Max Energy of 120.</summary>
    /// <remarks>
    /// ⚠️ 8 hours is 120 units, and 120 is Max Energy at Legend Level <b>0</b>. At Legend Level 1 —
    /// the lowest a player can actually be (`07` §1.1) — Max Energy is 122 under the authored
    /// formula and the true refill time is 8h08m, which the companion case below states rather than
    /// hides. `10` §3's "8 hours" is one of the four numbers the registered contradiction is about.
    /// </remarks>
    [Fact]
    public void Eight_hours_fills_an_empty_bar_at_the_base_max()
    {
        var accrued = EnergyMath.Accrue(
            Shipped, legendLevel: 0, new EnergyBanks(0, 0), TimeSpan.FromHours(8));

        accrued.Banks.ShouldBe(new EnergyBanks(120, 0));
        accrued.AnchorAdvance.ShouldBe(TimeSpan.FromHours(8));
    }

    /// <summary>
    /// 🔒 The same claim at the lowest Legend Level a player can hold. Eight hours leaves them two
    /// Energy short of the 122 the authored formula gives them, and it takes 8h08m to fill.
    /// </summary>
    [Fact]
    public void At_the_lowest_reachable_legend_level_eight_hours_leaves_the_bar_two_short()
    {
        var atEightHours = EnergyMath.Accrue(
            Shipped, legendLevel: 1, new EnergyBanks(0, 0), TimeSpan.FromHours(8));

        atEightHours.Banks.ShouldBe(new EnergyBanks(120, 0));
        EnergyMath.MaxEnergy(Shipped, legendLevel: 1).ShouldBe(122);

        var atFull = EnergyMath.Accrue(
            Shipped, legendLevel: 1, new EnergyBanks(0, 0), TimeSpan.FromHours(8) + TimeSpan.FromMinutes(8));

        atFull.Banks.ShouldBe(new EnergyBanks(122, 0));
    }

    /// <summary>`10` §3 — one Energy per four minutes, and nothing for the three minutes before it.</summary>
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    [InlineData(7, 1)]
    [InlineData(8, 2)]
    [InlineData(60, 15)]
    public void Regeneration_is_one_energy_per_four_minutes(int elapsedMinutes, int expectedEnergy)
    {
        var accrued = EnergyMath.Accrue(
            Shipped, legendLevel: 0, new EnergyBanks(0, 0), TimeSpan.FromMinutes(elapsedMinutes));

        accrued.Banks.Energy.ShouldBe(expectedEnergy);
        accrued.AnchorAdvance.ShouldBe(TimeSpan.FromMinutes(expectedEnergy * 4));
    }

    /// <summary>
    /// 🔒 `28` C1 — the case the Reserve exists for. Two days away regenerates 720 Energy; before
    /// the Reserve, 520 of it was discarded. Now 200 sits in the bar and 200 in the Reserve, and
    /// only the remaining 320 is lost — `28` C2's "maximum banked value: 400 Energy".
    /// </summary>
    [Fact]
    public void Two_days_offline_banks_a_full_bar_and_a_full_reserve()
    {
        var accrued = EnergyMath.Accrue(
            Shipped, legendLevel: 40, new EnergyBanks(0, 0), TimeSpan.FromDays(2));

        accrued.Banks.ShouldBe(new EnergyBanks(200, 200));
        accrued.AnchorAdvance.ShouldBe(TimeSpan.FromDays(2));
    }

    /// <summary>
    /// 🔒 The overflow is discarded past the Reserve cap rather than growing it without bound.
    /// Three weeks away is 7,560 Energy; `28` C2 is explicit that a player returning after three
    /// weeks must not find a month of content stacked up.
    /// </summary>
    [Fact]
    public void Overflow_past_the_reserve_cap_is_discarded_and_the_reserve_does_not_grow()
    {
        var threeWeeks = EnergyMath.Accrue(
            Shipped, legendLevel: 40, new EnergyBanks(0, 0), TimeSpan.FromDays(21));

        var twoDays = EnergyMath.Accrue(
            Shipped, legendLevel: 40, new EnergyBanks(0, 0), TimeSpan.FromDays(2));

        // The interesting claim, and the only one here: ten times the absence banks nothing more.
        // A literal (200, 200) on both would be two copies of the case above it.
        threeWeeks.Banks.ShouldBe(
            twoDays.Banks,
            "21 days banked more than 2 days did, so the cap is not holding and 28 C2's " +
            "'maximum banked value: 400 Energy' is not what the code does.");
    }

    /// <summary>
    /// 🔒 `28` C2 — "Regenerates: ❌ Never on its own. The Reserve only ever receives what the main
    /// bar could not hold."
    /// </summary>
    /// <remarks>
    /// ⚠️ Stated from a state where the Reserve has <b>room</b> and the main bar is <b>not</b>
    /// full, which is the only shape that can tell the difference. This case originally accrued
    /// into two already-full banks and asserted nothing moved — true, and useless: a Reserve given
    /// its own regeneration term is still capped, so the full pair reports the same answer either
    /// way. Proved by mutation: adding an independent Reserve regeneration to <c>Accrue</c> turned
    /// ten other cases red and left the one named for the property green.
    /// </remarks>
    [Fact]
    public void The_reserve_never_regenerates_on_its_own()
    {
        // Four hours is 60 units. The bar is 100 short of its 200 and the Reserve is 150 short of
        // its own, so a Reserve that regenerated independently would have somewhere to put it.
        var accrued = EnergyMath.Accrue(
            Shipped, legendLevel: 40, new EnergyBanks(100, 50), TimeSpan.FromHours(4));

        accrued.Banks.Reserve.ShouldBe(
            50,
            "the main bar had room for every one of the 60 units, so 28 C2 leaves the Reserve " +
            "untouched: it receives only what the bar could not hold.");
        accrued.Banks.Energy.ShouldBe(160);
    }

    /// <summary>
    /// The corollary at the ceiling: with both banks full there is nothing the bar could not hold
    /// that the Reserve has room for, so ten hours of regeneration move nothing at all.
    /// </summary>
    [Fact]
    public void Regeneration_into_two_full_banks_moves_nothing()
    {
        var full = new EnergyBanks(200, 200);

        var accrued = EnergyMath.Accrue(Shipped, legendLevel: 40, full, TimeSpan.FromHours(10));

        accrued.Banks.ShouldBe(full);
    }

    /// <summary>
    /// 🔒 A1 — the anchor advances even when nothing could be stored. Were it not to, a player who
    /// idled at full for a week would find the whole week's regeneration waiting for them the
    /// instant they spent a single point.
    /// </summary>
    [Fact]
    public void The_anchor_advances_even_when_both_banks_are_full()
    {
        var accrued = EnergyMath.Accrue(
            Shipped, legendLevel: 40, new EnergyBanks(200, 200), TimeSpan.FromHours(10));

        accrued.AnchorAdvance.ShouldBe(TimeSpan.FromHours(10));
    }

    /// <summary>
    /// 🔒 `28` C2 — the Reserve "fills only while the main bar is at maximum". A partially full bar
    /// takes everything up to its own maximum before a single point reaches the Reserve.
    /// </summary>
    [Fact]
    public void The_main_bar_fills_before_the_reserve_takes_anything()
    {
        // 30 minutes is 7 whole units; the bar is 5 short of its 200.
        var accrued = EnergyMath.Accrue(
            Shipped, legendLevel: 40, new EnergyBanks(195, 0), TimeSpan.FromMinutes(30));

        accrued.Banks.ShouldBe(new EnergyBanks(200, 2));
    }

    /// <summary>
    /// A Reserve multiple of zero is the "no Reserve" configuration. Everything the bar cannot hold
    /// is discarded, exactly as `10` §3 read before `28` C resolved it.
    /// </summary>
    [Fact]
    public void With_no_reserve_authored_the_overflow_is_simply_discarded()
    {
        var noReserve = EnergyTuning.Read(
            ProgressionDocuments.With(reserveMultipleOfMax: ContentValue.Number(0)));

        var accrued = EnergyMath.Accrue(
            noReserve, legendLevel: 40, new EnergyBanks(0, 0), TimeSpan.FromDays(2));

        accrued.Banks.ShouldBe(new EnergyBanks(200, 0));
    }

    [Fact]
    public void A_negative_elapsed_span_is_refused()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => EnergyMath.Accrue(
                Shipped, legendLevel: 0, new EnergyBanks(0, 0), TimeSpan.FromMinutes(-4)));

        thrown.ParamName.ShouldBe("sinceAnchor");
    }

    // ------------------------------------------------------------------ grants

    // ⚠️ The three cases below use `10` §3.1's amounts (40 / 20 / 10) as literals, and they are
    // named for the cascade rather than for the source: nothing here reads
    // `progression.json#/energy/sources`, so retuning AD_ENERGY to 30 must not turn a case called
    // "an ad grant of forty" red for the wrong reason. The authored amounts and their per-day caps
    // are pinned where they live, by EnergyTuningMatchesTuningDataTests in Application.Tests.

    /// <summary>`10` §3.1 — a fixed grant fills the main bar before anything else.</summary>
    [Fact]
    public void A_grant_fills_the_bar_first()
    {
        var granted = EnergyMath.Grant(Shipped, legendLevel: 40, new EnergyBanks(140, 0), 40);

        granted.ShouldBe(new EnergyBanks(180, 0));
    }

    /// <summary>
    /// `28` C2 — "`AD_ENERGY` grants +40 to the main bar and overflows into the Reserve exactly like
    /// any other source". Twenty of the forty do not fit, and they bank rather than vanish.
    /// </summary>
    [Fact]
    public void A_grant_the_bar_cannot_hold_overflows_into_the_reserve()
    {
        var granted = EnergyMath.Grant(Shipped, legendLevel: 40, new EnergyBanks(180, 0), 40);

        granted.ShouldBe(new EnergyBanks(200, 20));
    }

    /// <summary>Past the Reserve cap the remainder is discarded, not carried anywhere.</summary>
    [Fact]
    public void A_grant_past_the_reserve_cap_is_discarded()
    {
        var granted = EnergyMath.Grant(Shipped, legendLevel: 40, new EnergyBanks(200, 190), 40);

        granted.ShouldBe(new EnergyBanks(200, 200));
    }

    /// <summary>Every `10` §3.1 source routes through the one cascade; only the amount differs.</summary>
    [Theory]
    [InlineData(20, 100, 120)]
    [InlineData(10, 100, 110)]
    public void A_grant_of_any_size_routes_through_the_same_cascade(int amount, int from, int expected)
    {
        EnergyMath.Grant(Shipped, legendLevel: 40, new EnergyBanks(from, 0), amount)
            .ShouldBe(new EnergyBanks(expected, 0));
    }

    [Fact]
    public void A_grant_of_nothing_changes_nothing()
    {
        var banks = new EnergyBanks(37, 11);

        EnergyMath.Grant(Shipped, legendLevel: 40, banks, 0).ShouldBe(banks);
    }

    [Fact]
    public void A_negative_grant_is_refused()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => EnergyMath.Grant(Shipped, legendLevel: 40, new EnergyBanks(0, 0), -1));

        thrown.ParamName.ShouldBe("amount");
    }

    // ------------------------------------------------------------------ refill to full

    /// <summary>
    /// `10` §3.1 — the daily free refill and the Legend Level-up refill both grant "to full".
    /// </summary>
    [Fact]
    public void A_refill_to_full_tops_the_main_bar_up()
    {
        EnergyMath.RefillToFull(Shipped, legendLevel: 40, new EnergyBanks(60, 30))
            .ShouldBe(new EnergyBanks(200, 30));
    }

    /// <summary>
    /// 🔒 <b>The ruling on "to full, overflowing".</b> `28` C2 lists daily refills and level-up
    /// refills among the sources that overflow into the Reserve, and `10` §3.1 authors their amount
    /// as "to full". "To full" of a bar that is already full is <b>zero</b>, so such a refill grants
    /// nothing and therefore overflows nothing.
    /// <para>
    /// The alternative reading — that a refill grants a whole Max Energy regardless of the bar, so
    /// a full-bar player banks an entire second tank — would require inventing an amount (<c>+max</c>)
    /// that no document authors, which S6 forbids. `28` C2's list is naming the sources that route
    /// through the overflow cascade, not claiming each one always produces overflow; regeneration,
    /// quest grants and ad grants demonstrably do.
    /// </para>
    /// </summary>
    [Fact]
    public void A_refill_of_an_already_full_bar_grants_nothing_and_banks_nothing()
    {
        var full = new EnergyBanks(200, 50);

        EnergyMath.RefillToFull(Shipped, legendLevel: 40, full).ShouldBe(full);
    }

    /// <summary>
    /// The refill routes through the same cascade as every other source, which is the half of
    /// `28` C2's sentence that is true: one point short of full, the refill grants exactly that one
    /// point and the Reserve is untouched.
    /// </summary>
    [Fact]
    public void A_refill_grants_exactly_the_deficit_and_never_more()
    {
        EnergyMath.RefillToFull(Shipped, legendLevel: 40, new EnergyBanks(199, 0))
            .ShouldBe(new EnergyBanks(200, 0));
    }

    /// <summary>A refill is measured against the player's current Max Energy, not against 200.</summary>
    [Fact]
    public void A_refill_fills_to_the_current_max_not_to_the_cap()
    {
        EnergyMath.RefillToFull(Shipped, legendLevel: 0, new EnergyBanks(0, 0))
            .ShouldBe(new EnergyBanks(120, 0));
    }

    // ------------------------------------------------------------------ spending

    /// <summary>`28` C2 — "a run draws from the main bar first".</summary>
    [Fact]
    public void A_run_draws_from_the_main_bar_first()
    {
        var spend = EnergyMath.Spend(new EnergyBanks(200, 200), 20);

        spend.IsAffordable.ShouldBeTrue();
        spend.Banks.ShouldBe(new EnergyBanks(180, 200));
        spend.DrawnFromBar.ShouldBe(20);
        spend.DrawnFromReserve.ShouldBe(0);
    }

    /// <summary>`28` C2 — "then from the Reserve for any shortfall. There is no button and no decision."</summary>
    [Fact]
    public void The_reserve_covers_only_the_shortfall()
    {
        var spend = EnergyMath.Spend(new EnergyBanks(10, 200), 20);

        spend.IsAffordable.ShouldBeTrue();
        spend.Banks.ShouldBe(new EnergyBanks(0, 190));
        spend.DrawnFromBar.ShouldBe(10);
        spend.DrawnFromReserve.ShouldBe(10);
    }

    [Fact]
    public void An_empty_bar_draws_the_whole_cost_from_the_reserve()
    {
        var spend = EnergyMath.Spend(new EnergyBanks(0, 20), 20);

        spend.Banks.ShouldBe(new EnergyBanks(0, 0));
        spend.DrawnFromBar.ShouldBe(0);
        spend.DrawnFromReserve.ShouldBe(20);
    }

    /// <summary>
    /// 🔒 Unaffordable is a value, not an exception (`30` §2.1), and it leaves both banks exactly
    /// where they were — a partial draw would charge a player for a run they never got.
    /// </summary>
    [Fact]
    public void A_cost_the_two_banks_together_cannot_cover_is_refused_and_takes_nothing()
    {
        var banks = new EnergyBanks(10, 5);

        var spend = EnergyMath.Spend(banks, 20);

        spend.IsAffordable.ShouldBeFalse();
        spend.Banks.ShouldBe(banks);
        spend.DrawnFromBar.ShouldBe(0);
        spend.DrawnFromReserve.ShouldBe(0);
    }

    /// <summary>Exactly enough is enough — the boundary, on the affordable side.</summary>
    [Fact]
    public void A_cost_the_two_banks_cover_exactly_is_affordable()
    {
        var spend = EnergyMath.Spend(new EnergyBanks(10, 10), 20);

        spend.IsAffordable.ShouldBeTrue();
        spend.Banks.ShouldBe(new EnergyBanks(0, 0));
    }

    /// <summary>One short is short — the boundary, on the refused side.</summary>
    [Fact]
    public void A_cost_one_above_the_two_banks_is_refused()
    {
        EnergyMath.Spend(new EnergyBanks(10, 9), 20).IsAffordable.ShouldBeFalse();
    }

    /// <summary>`10` §3 — "Runs on a full tank: 6", at the base Max Energy of 120 and 20 per run.</summary>
    /// <remarks>
    /// ⚠️ Exact at Legend Level 0. At Legend Level 1, the lowest a player can hold, the authored
    /// formula gives 122 and the sixth run leaves 2 Energy stranded — still six runs, but not the
    /// clean division `10` §3 states. Part of the registered contradiction.
    /// </remarks>
    [Fact]
    public void A_full_base_tank_pays_for_exactly_six_runs()
    {
        var (runs, left) = RunsAffordableFrom(new EnergyBanks(EnergyMath.MaxEnergy(Shipped, 0), 0));

        runs.ShouldBe(6);
        left.ShouldBe(new EnergyBanks(0, 0));

        var (atLevelOne, stranded) =
            RunsAffordableFrom(new EnergyBanks(EnergyMath.MaxEnergy(Shipped, 1), 0));

        atLevelOne.ShouldBe(6);
        stranded.ShouldBe(new EnergyBanks(2, 0));
    }

    /// <summary>
    /// `28` C2 — "Maximum banked value: 400 Energy = 20 runs". The bar and the Reserve both full at
    /// the cap pay for twenty runs and not a twenty-first.
    /// </summary>
    [Fact]
    public void A_full_bar_and_a_full_reserve_pay_for_exactly_twenty_runs()
    {
        var (runs, left) = RunsAffordableFrom(new EnergyBanks(200, 200));

        runs.ShouldBe(20);
        left.ShouldBe(new EnergyBanks(0, 0));
    }

    /// <summary>
    /// Spends <c>runCost</c> until the two banks together cannot pay, and reports how many runs
    /// that bought. Bounded rather than a <c>while (true)</c>: a <c>Spend</c> that reported
    /// affordable without drawing anything would otherwise hang the suite instead of failing it.
    /// </summary>
    private static (int Runs, EnergyBanks Left) RunsAffordableFrom(EnergyBanks banks)
    {
        const int impossiblyMany = 1000;

        for (var runs = 0; runs <= impossiblyMany; runs++)
        {
            var spend = EnergyMath.Spend(banks, Shipped.RunCost);
            if (!spend.IsAffordable)
            {
                return (runs, banks);
            }

            banks = spend.Banks;
        }

        throw new InvalidOperationException(
            $"Spend reported {impossiblyMany} affordable runs from two banks that hold at most 400 " +
            "Energy, so it is reporting affordable without drawing anything.");
    }

    [Fact]
    public void A_negative_cost_is_refused()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => EnergyMath.Spend(new EnergyBanks(0, 0), -1));

        thrown.ParamName.ShouldBe("cost");
    }

    // ------------------------------------------------------------------ banks as a value

    /// <summary>
    /// 🔒 S2 — the two guards are separate, so each case pins which one fired. Collapsing them into
    /// one that always reports <c>energy</c> would otherwise stay green with a wrong diagnostic.
    /// </summary>
    [Theory]
    [InlineData(-1, 0, "energy")]
    [InlineData(0, -1, "reserve")]
    public void Neither_bank_can_hold_a_negative_amount(int energy, int reserve, string parameter)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => new EnergyBanks(energy, reserve));

        thrown.ParamName.ShouldBe(parameter);
    }

    /// <summary>
    /// 🔒 `28` C2's UI reading, and the three hand-written <c>PrintMembers</c> overrides that
    /// produce it. Each replaces a synthesized one — <c>EnergyBanks</c>' would print nothing at all,
    /// both its members being <c>internal</c> — and until this case only a <em>failing</em>
    /// assertion ever invoked them. The repo's precedent is
    /// <c>GameContextTests.ToString_renders_identically_under_any_culture</c>.
    /// </summary>
    [Fact]
    public void The_three_energy_values_render_their_own_diagnostics()
    {
        // Shouldly's string ShouldBe is exact equality — no Case parameter, and none needed.
        new EnergyBanks(138, 200).ToString()
            .ShouldBe("EnergyBanks { 138 (+200) }");

        EnergyMath.Accrue(Shipped, legendLevel: 40, new EnergyBanks(0, 0), TimeSpan.FromHours(2))
            .ToString()
            .ShouldBe("EnergyAccrual { Banks = EnergyBanks { 30 (+0) }, AnchorAdvance = 02:00:00 }");

        EnergyMath.Spend(new EnergyBanks(10, 200), 20).ToString()
            .ShouldBe(
                "EnergySpend { paid 10 from the bar and 10 from the reserve, " +
                "leaving EnergyBanks { 0 (+190) } }");

        EnergyMath.Spend(new EnergyBanks(10, 5), 20).ToString()
            .ShouldBe("EnergySpend { unaffordable from EnergyBanks { 10 (+5) } }");
    }

    /// <summary>
    /// 🔒 `30` §11.5 puts "Energy never exceeds max + reserve" on the <b>aggregate</b>, not here.
    /// A balance patch that lowered <c>baseMax</c> would otherwise make every rule throw for every
    /// player already above the new maximum. The rules neither confiscate the excess nor add to it:
    /// the headroom is simply zero, and the player drains back under the cap by playing.
    /// </summary>
    [Fact]
    public void Banks_above_the_current_maximum_are_left_alone_rather_than_confiscated()
    {
        var overCap = new EnergyBanks(260, 260);

        EnergyMath.Grant(Shipped, legendLevel: 40, overCap, 40).ShouldBe(overCap);
        EnergyMath.RefillToFull(Shipped, legendLevel: 40, overCap).ShouldBe(overCap);
        EnergyMath.Accrue(Shipped, legendLevel: 40, overCap, TimeSpan.FromHours(4)).Banks.ShouldBe(overCap);
        EnergyMath.Spend(overCap, 20).Banks.ShouldBe(new EnergyBanks(240, 260));
    }

    /// <summary>
    /// 🔒 X-02 / `30` §3 — energy is not a Plus benefit. `28` C2: "`AD_ENERGY` … capped, and
    /// identical for Plus." Nothing in this surface takes an entitlement, and
    /// <c>IsolationTests.Entitlements_are_unreachable_from_the_rules_and_the_power_computation</c>
    /// enforces that structurally from this commit onward — this case states the intent the
    /// architecture rule protects.
    /// </summary>
    [Fact]
    public void No_energy_rule_takes_an_entitlement_or_a_game_context()
    {
        const BindingFlags declared =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var surface = new[]
        {
            typeof(EnergyMath), typeof(EnergyTuning), typeof(EnergyBanks),
            typeof(EnergyAccrual), typeof(EnergySpend),
        };

        var methods = surface.SelectMany(t => t.GetMethods(declared)).ToArray();
        var parameters = methods.SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType.Name)
            .ToArray();

        // 🔒 The floor, and it is the ENTRY POINTS rather than a count. `ShouldNotBeEmpty` was the
        // floor here and could not fail: without DeclaredOnly, GetMethods returns object.Equals and
        // the compiler-generated record-struct plumbing, so deleting every authored method from all
        // five types still left the array non-empty. Naming the six operations that 10 §3 and 28
        // Part C between them specify is a floor a rename or a deletion actually breaks.
        methods
            .Where(m => m.DeclaringType == typeof(EnergyMath) && m.IsAssembly && m.IsStatic)
            .Select(m => m.Name)
            .ShouldBe(
                new[] { "MaxEnergy", "ReserveCapacity", "Accrue", "Grant", "RefillToFull", "Spend" },
                ignoreOrder: true,
                "these are the six operations 10 §3 and 28 Part C between them specify, and they " +
                "are the subject set this case quantifies over. One gone, or a seventh arrived, " +
                "means the surface moved and the two assertions below no longer cover it.");

        parameters.ShouldNotContain(nameof(Entitlements));
        parameters.ShouldNotContain(nameof(GameContext));
    }
}
