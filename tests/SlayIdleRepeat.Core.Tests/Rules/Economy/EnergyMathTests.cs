using System.Reflection;
using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.Core.Rules.Economy;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Economy;

/// <summary>
/// Max Energy, regeneration, overflow routing, grants, refills and the main-bar-first spend
/// order.
/// </summary>
/// <remarks>
/// The accrual rule itself — whole units only, anchor advanced by <c>units × interval</c> and
/// never to "now" — is <see cref="EnergyAccrualPropertyTests"/>'. This covers the arithmetic
/// around it.
/// </remarks>
public sealed class EnergyMathTests
{
    private static readonly EnergyTuning Shipped = EnergyTuning.Read(ProgressionDocuments.Shipped);

    // ------------------------------------------------------------------ Max Energy

    /// <summary>Max Energy is 120 (+2 per Legend Level, cap 200).</summary>
    /// <remarks>
    /// Row <c>(1, 120)</c> is the headline. The increment counts levels <em>gained</em>, so a
    /// starting player at Legend Level 1 has exactly 120 — which is what makes "8 hours from
    /// empty" and "6 runs on a full tank" exact rather than approximate. Row <c>(41, 200)</c> is
    /// where the cap first binds.
    /// </remarks>
    [Theory]
    [InlineData(1, 120)]
    [InlineData(2, 122)]
    [InlineData(11, 140)]
    [InlineData(40, 198)]
    [InlineData(41, 200)]
    [InlineData(42, 200)]
    [InlineData(200, 200)]
    public void Max_energy_is_120_plus_2_per_legend_level_capped_at_200(int legendLevel, int expected) =>
        EnergyMath.MaxEnergy(Shipped, legendLevel).ShouldBe(expected);

    /// <summary>
    /// The three numbers that are arithmetic on Max Energy, checked against a starting player
    /// rather than a level no player occupies. All three are off by one increment if the formula
    /// ever counts levels held instead of levels gained.
    /// </summary>
    [Fact]
    public void A_starting_players_tank_makes_every_10_3_number_exact()
    {
        var max = EnergyMath.MaxEnergy(Shipped, legendLevel: 1);

        max.ShouldBe(120, "10 §3: Max Energy 120, and 10 §3.2's budget line '120 (start)'.");

        (max / Shipped.RunCost).ShouldBe(6, "10 §3: runs on a full tank, 6 — exactly, no remainder.");
        (max * Shipped.RegenInterval).ShouldBe(
            TimeSpan.FromHours(8), "10 §3: full refill time, 8 hours from empty — exactly.");
    }

    /// <summary>
    /// The Reserve holds 1× Max Energy, not a hard-coded 200. At a low Legend Level the two are
    /// 120, and a reader that had baked in the cap would say 200 here.
    /// </summary>
    [Theory]
    [InlineData(1, 120)]
    [InlineData(2, 122)]
    [InlineData(40, 198)]
    [InlineData(41, 200)]
    [InlineData(200, 200)]
    public void Reserve_capacity_tracks_current_max_energy_not_the_cap(int legendLevel, int expected)
    {
        EnergyMath.ReserveCapacity(Shipped, legendLevel).ShouldBe(expected);
        EnergyMath.ReserveCapacity(Shipped, legendLevel)
            .ShouldBe(EnergyMath.MaxEnergy(Shipped, legendLevel));
    }

    /// <summary>Raising the reserve multiple dial grows the Reserve with it.</summary>
    [Fact]
    public void Raising_the_reserve_multiple_raises_the_reserve_capacity()
    {
        var doubled = EnergyTuning.Read(
            ProgressionDocuments.With(reserveMultipleOfMax: ContentValue.Number(2)));

        EnergyMath.ReserveCapacity(doubled, legendLevel: 41).ShouldBe(400);
        EnergyMath.MaxEnergy(doubled, legendLevel: 41).ShouldBe(200);
    }

    /// <summary>
    /// 🔒 <b>Every</b> entry point guards the Legend Level, not just the one that uses it directly:
    /// without a case each, deleting <c>Accrue</c>'s or <c>Grant</c>'s call changes nothing observable
    /// (the nested <c>MaxEnergy</c> still throws) and no test notices.
    /// </summary>
    /// <remarks>
    /// Zero is refused, not just negatives: the increment counts levels gained, so Legend Level 0
    /// would subtract one and hand back 118 — a plausible number for a state that cannot exist.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryEntryPoint))]
    public void A_legend_level_below_one_is_refused_by_every_entry_point(
        string name, Action<int> call)
    {
        foreach (var below in new[] { 0, -1 })
        {
            var thrown = Should.Throw<ArgumentOutOfRangeException>(() => call(below));

            thrown.ParamName.ShouldBe(
                "legendLevel", $"{name} did not name the offending parameter for {below}.");
        }
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
    /// Every economy number is swept by a placeholder-sweeping simulator. The 64-bit widening in
    /// <c>MaxEnergy</c> and the <c>int.MaxValue</c> clamp in <c>ReserveCapacity</c> were both
    /// unreachable from this suite before this case — reverting either to 32-bit arithmetic left
    /// everything green.
    /// </summary>
    [Fact]
    public void A_swept_per_level_increment_cannot_wrap_max_energy_negative()
    {
        var swept = EnergyTuning.Read(
            ProgressionDocuments.With(perLegendLevel: ContentValue.Number(2_000_000_000)));

        // In 32-bit, 120 + 2e9 wraps to about -2.29 billion, which then wins Math.Min against the
        // cap and hands every rule below it a negative Max Energy.
        EnergyMath.MaxEnergy(swept, legendLevel: 2).ShouldBe(200);
        EnergyMath.MaxEnergy(swept, legendLevel: 200).ShouldBe(200);
    }

    /// <summary>The companion clamp: the reserve multiple dial cannot multiply the Reserve past an int.</summary>
    [Fact]
    public void A_swept_reserve_multiple_clamps_rather_than_wrapping()
    {
        var swept = EnergyTuning.Read(
            ProgressionDocuments.With(reserveMultipleOfMax: ContentValue.Number(2_000_000_000)));

        EnergyMath.ReserveCapacity(swept, legendLevel: 41).ShouldBe(int.MaxValue);
    }

    // ------------------------------------------------------------------ accrual arithmetic

    /// <summary>Full refill time is 8 hours from empty, at a starting player's tank.</summary>
    /// <remarks>
    /// Eight hours is exactly 120 units and a starting maximum is exactly 120, so the bar fills
    /// to the brim and not one unit past or short. Under <c>× legendLevel</c> the same case would
    /// leave them on 120 of 122.
    /// </remarks>
    [Fact]
    public void Eight_hours_fills_a_starting_players_empty_bar_exactly()
    {
        var accrued = EnergyMath.Accrue(
            Shipped, legendLevel: 1, new EnergyBanks(0, 0), TimeSpan.FromHours(8));

        accrued.Banks.ShouldBe(new EnergyBanks(120, 0));
        accrued.Banks.Energy.ShouldBe(EnergyMath.MaxEnergy(Shipped, legendLevel: 1));
        accrued.AnchorAdvance.ShouldBe(TimeSpan.FromHours(8));

        // One unit short of eight hours is one Energy short of full — the boundary from below.
        EnergyMath.Accrue(
                Shipped, legendLevel: 1, new EnergyBanks(0, 0),
                TimeSpan.FromHours(8) - TimeSpan.FromMinutes(4))
            .Banks.ShouldBe(new EnergyBanks(119, 0));
    }

    /// <summary>One Energy per four minutes, and nothing for the three minutes before it.</summary>
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
            Shipped, legendLevel: 1, new EnergyBanks(0, 0), TimeSpan.FromMinutes(elapsedMinutes));

        accrued.Banks.Energy.ShouldBe(expectedEnergy);
        accrued.AnchorAdvance.ShouldBe(TimeSpan.FromMinutes(expectedEnergy * 4));
    }

    /// <summary>
    /// The case the Reserve exists for. Two days away regenerates 720 Energy; before the
    /// Reserve, 520 of it was discarded. Now 200 sits in the bar and 200 in the Reserve, and only
    /// the remaining 320 is lost, matching the maximum banked value of 400 Energy.
    /// </summary>
    [Fact]
    public void Two_days_offline_banks_a_full_bar_and_a_full_reserve()
    {
        var accrued = EnergyMath.Accrue(
            Shipped, legendLevel: 41, new EnergyBanks(0, 0), TimeSpan.FromDays(2));

        accrued.Banks.ShouldBe(new EnergyBanks(200, 200));
        accrued.AnchorAdvance.ShouldBe(TimeSpan.FromDays(2));
    }

    /// <summary>
    /// The overflow is discarded past the Reserve cap rather than growing it without bound:
    /// three weeks away is 7,560 Energy, and a player returning must not find a month of content
    /// stacked up.
    /// </summary>
    [Fact]
    public void Overflow_past_the_reserve_cap_is_discarded_and_the_reserve_does_not_grow()
    {
        var threeWeeks = EnergyMath.Accrue(
            Shipped, legendLevel: 41, new EnergyBanks(0, 0), TimeSpan.FromDays(21));

        var twoDays = EnergyMath.Accrue(
            Shipped, legendLevel: 41, new EnergyBanks(0, 0), TimeSpan.FromDays(2));

        // The interesting claim, and the only one here: ten times the absence banks nothing more.
        // A literal (200, 200) on both would be two copies of the case above it.
        threeWeeks.Banks.ShouldBe(
            twoDays.Banks,
            "21 days banked more than 2 days did, so the cap is not holding and 28 C2's " +
            "'maximum banked value: 400 Energy' is not what the code does.");
    }

    /// <summary>
    /// The Reserve never regenerates on its own — it only ever receives what the main bar could
    /// not hold.
    /// </summary>
    /// <remarks>
    /// Stated from a state where the Reserve has room and the main bar is not full, the only
    /// shape that can tell the difference: accruing into two already-full banks asserts nothing,
    /// since a Reserve with its own regeneration term is still capped and reports the same
    /// answer either way.
    /// </remarks>
    [Fact]
    public void The_reserve_never_regenerates_on_its_own()
    {
        // Four hours is 60 units. The bar is 100 short of its 200 and the Reserve is 150 short of
        // its own, so a Reserve that regenerated independently would have somewhere to put it.
        var accrued = EnergyMath.Accrue(
            Shipped, legendLevel: 41, new EnergyBanks(100, 50), TimeSpan.FromHours(4));

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

        var accrued = EnergyMath.Accrue(Shipped, legendLevel: 41, full, TimeSpan.FromHours(10));

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
            Shipped, legendLevel: 41, new EnergyBanks(200, 200), TimeSpan.FromHours(10));

        accrued.AnchorAdvance.ShouldBe(TimeSpan.FromHours(10));
    }

    /// <summary>
    /// The Reserve fills only while the main bar is at maximum: a partially full bar takes
    /// everything up to its own maximum before a single point reaches the Reserve.
    /// </summary>
    [Fact]
    public void The_main_bar_fills_before_the_reserve_takes_anything()
    {
        // 30 minutes is 7 whole units; the bar is 5 short of its 200.
        var accrued = EnergyMath.Accrue(
            Shipped, legendLevel: 41, new EnergyBanks(195, 0), TimeSpan.FromMinutes(30));

        accrued.Banks.ShouldBe(new EnergyBanks(200, 2));
    }

    /// <summary>
    /// A Reserve multiple of zero is the "no Reserve" configuration: everything the bar cannot
    /// hold is discarded.
    /// </summary>
    [Fact]
    public void With_no_reserve_authored_the_overflow_is_simply_discarded()
    {
        var noReserve = EnergyTuning.Read(
            ProgressionDocuments.With(reserveMultipleOfMax: ContentValue.Number(0)));

        var accrued = EnergyMath.Accrue(
            noReserve, legendLevel: 41, new EnergyBanks(0, 0), TimeSpan.FromDays(2));

        accrued.Banks.ShouldBe(new EnergyBanks(200, 0));
    }

    [Fact]
    public void A_negative_elapsed_span_is_refused()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => EnergyMath.Accrue(
                Shipped, legendLevel: 1, new EnergyBanks(0, 0), TimeSpan.FromMinutes(-4)));

        thrown.ParamName.ShouldBe("sinceAnchor");
    }

    // ------------------------------------------------------------------ grants

    // The three cases below use the authored amounts (40 / 20 / 10) as literals, and are named
    // for the cascade rather than for the source: nothing here reads
    // `progression.json#/energy/sources`, so retuning AD_ENERGY to 30 must not turn a case called
    // "an ad grant of forty" red for the wrong reason. The authored amounts and their per-day caps
    // are pinned separately, by EnergyTuningMatchesTuningDataTests in Application.Tests.

    /// <summary>A fixed grant fills the main bar before anything else.</summary>
    [Fact]
    public void A_grant_fills_the_bar_first()
    {
        var granted = EnergyMath.Grant(Shipped, legendLevel: 41, new EnergyBanks(140, 0), 40);

        granted.ShouldBe(new EnergyBanks(180, 0));
    }

    /// <summary>
    /// A grant overflows into the Reserve exactly like any other source. Twenty of the forty do
    /// not fit, and they bank rather than vanish.
    /// </summary>
    [Fact]
    public void A_grant_the_bar_cannot_hold_overflows_into_the_reserve()
    {
        var granted = EnergyMath.Grant(Shipped, legendLevel: 41, new EnergyBanks(180, 0), 40);

        granted.ShouldBe(new EnergyBanks(200, 20));
    }

    /// <summary>Past the Reserve cap the remainder is discarded, not carried anywhere.</summary>
    [Fact]
    public void A_grant_past_the_reserve_cap_is_discarded()
    {
        var granted = EnergyMath.Grant(Shipped, legendLevel: 41, new EnergyBanks(200, 190), 40);

        granted.ShouldBe(new EnergyBanks(200, 200));
    }

    /// <summary>Every source routes through the one cascade; only the amount differs.</summary>
    [Theory]
    [InlineData(20, 100, 120)]
    [InlineData(10, 100, 110)]
    public void A_grant_of_any_size_routes_through_the_same_cascade(int amount, int from, int expected)
    {
        EnergyMath.Grant(Shipped, legendLevel: 41, new EnergyBanks(from, 0), amount)
            .ShouldBe(new EnergyBanks(expected, 0));
    }

    [Fact]
    public void A_grant_of_nothing_changes_nothing()
    {
        var banks = new EnergyBanks(37, 11);

        EnergyMath.Grant(Shipped, legendLevel: 41, banks, 0).ShouldBe(banks);
    }

    [Fact]
    public void A_negative_grant_is_refused()
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(
            () => EnergyMath.Grant(Shipped, legendLevel: 41, new EnergyBanks(0, 0), -1));

        thrown.ParamName.ShouldBe("amount");
    }

    // ------------------------------------------------------------------ refill to full

    /// <summary>The daily free refill and the Legend Level-up refill both grant "to full".</summary>
    [Fact]
    public void A_refill_to_full_tops_the_main_bar_up()
    {
        EnergyMath.RefillToFull(Shipped, legendLevel: 41, new EnergyBanks(60, 30))
            .ShouldBe(new EnergyBanks(200, 30));
    }

    /// <summary>
    /// 🔒 <b>The ruling on "to full, overflowing".</b> "To full" of a bar that is already full is
    /// <b>zero</b>, so such a refill grants nothing and therefore overflows nothing.
    /// </summary>
    /// <remarks>
    /// The alternative — a refill granting a whole Max Energy regardless of the bar, so a
    /// full-bar player banks a second tank — would require inventing an amount nothing authors.
    /// The listed sources route through the overflow cascade; they don't always produce overflow.
    /// </remarks>
    [Fact]
    public void A_refill_of_an_already_full_bar_grants_nothing_and_banks_nothing()
    {
        var full = new EnergyBanks(200, 50);

        EnergyMath.RefillToFull(Shipped, legendLevel: 41, full).ShouldBe(full);
    }

    /// <summary>
    /// The refill routes through the same cascade as every other source: one point short of
    /// full, the refill grants exactly that one point and the Reserve is untouched.
    /// </summary>
    [Fact]
    public void A_refill_grants_exactly_the_deficit_and_never_more()
    {
        EnergyMath.RefillToFull(Shipped, legendLevel: 41, new EnergyBanks(199, 0))
            .ShouldBe(new EnergyBanks(200, 0));
    }

    /// <summary>A refill is measured against the player's current Max Energy, not against 200.</summary>
    [Fact]
    public void A_refill_fills_to_the_current_max_not_to_the_cap()
    {
        EnergyMath.RefillToFull(Shipped, legendLevel: 1, new EnergyBanks(0, 0))
            .ShouldBe(new EnergyBanks(120, 0));
    }

    // ------------------------------------------------------------------ spending

    [Fact]
    public void A_run_draws_from_the_main_bar_first()
    {
        var spend = EnergyMath.Spend(new EnergyBanks(200, 200), 20);

        spend.IsAffordable.ShouldBeTrue();
        spend.Banks.ShouldBe(new EnergyBanks(180, 200));
        spend.DrawnFromBar.ShouldBe(20);
        spend.DrawnFromReserve.ShouldBe(0);
    }

    /// <summary>The Reserve covers any shortfall automatically — no button and no decision.</summary>
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
    /// Unaffordable is a value, not an exception, and it leaves both banks exactly where they
    /// were — a partial draw would charge a player for a run they never got.
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

    /// <summary>Runs on a full tank: 6, at a starting player's 120 and 20 per run.</summary>
    /// <remarks>
    /// The <c>left</c> assertion is the half that matters: 120 ÷ 20 strands nothing. Under
    /// <c>× legendLevel</c> a starting player holds 122 and finishes with 2 Energy they can never
    /// spend — six runs by count, but not a clean tank.
    /// </remarks>
    [Fact]
    public void A_starting_players_full_tank_pays_for_exactly_six_runs_with_nothing_left()
    {
        var (runs, left) = RunsAffordableFrom(
            new EnergyBanks(EnergyMath.MaxEnergy(Shipped, legendLevel: 1), 0));

        runs.ShouldBe(6);
        left.ShouldBe(new EnergyBanks(0, 0));
    }

    /// <summary>
    /// Maximum banked value: 400 Energy = 20 runs. The bar and the Reserve both full at the cap
    /// pay for twenty runs and not a twenty-first.
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
    /// The two guards are separate, so each case pins which one fired. Collapsing them into one
    /// that always reports <c>Energy</c> would otherwise stay green with a wrong diagnostic. The
    /// names are capitalised because they are positional-record parameters, the shape
    /// <c>CanonicalStateWriter</c> requires.
    /// </summary>
    [Theory]
    [InlineData(-1, 0, "Energy")]
    [InlineData(0, -1, "Reserve")]
    public void Neither_bank_can_hold_a_negative_amount(int energy, int reserve, string parameter)
    {
        var thrown = Should.Throw<ArgumentOutOfRangeException>(() => new EnergyBanks(energy, reserve));

        thrown.ParamName.ShouldBe(parameter);
    }

    /// <summary>
    /// The UI reading, and the three hand-written <c>PrintMembers</c> overrides that produce it.
    /// Each replaces a synthesized one — <c>EnergyBanks</c>' would print nothing at all, both its
    /// members being <c>internal</c> — and until this case only a failing assertion ever invoked
    /// them.
    /// </summary>
    [Fact]
    public void The_three_energy_values_render_their_own_diagnostics()
    {
        // Shouldly's string ShouldBe is exact equality — no Case parameter, and none needed.
        new EnergyBanks(138, 200).ToString()
            .ShouldBe("EnergyBanks { 138 (+200) }");

        EnergyMath.Accrue(Shipped, legendLevel: 41, new EnergyBanks(0, 0), TimeSpan.FromHours(2))
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
    /// "Energy never exceeds max + reserve" is enforced on the aggregate, not here. A balance
    /// patch that lowered <c>baseMax</c> would otherwise make every rule throw for every player
    /// already above the new maximum. The rules neither confiscate the excess nor add to it: the
    /// headroom is simply zero, and the player drains back under the cap by playing.
    /// </summary>
    [Fact]
    public void Banks_above_the_current_maximum_are_left_alone_rather_than_confiscated()
    {
        var overCap = new EnergyBanks(260, 260);

        EnergyMath.Grant(Shipped, legendLevel: 41, overCap, 40).ShouldBe(overCap);
        EnergyMath.RefillToFull(Shipped, legendLevel: 41, overCap).ShouldBe(overCap);
        EnergyMath.Accrue(Shipped, legendLevel: 41, overCap, TimeSpan.FromHours(4)).Banks.ShouldBe(overCap);
        EnergyMath.Spend(overCap, 20).Banks.ShouldBe(new EnergyBanks(240, 260));
    }

    /// <summary>
    /// Energy is not a Plus benefit: nothing in this surface takes an entitlement, and
    /// <c>IsolationTests.Entitlements_are_unreachable_from_the_rules_and_the_power_computation</c>
    /// enforces that structurally — this case states the intent the architecture rule protects.
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

        // The floor is the entry points, not a count: without DeclaredOnly, GetMethods returns
        // object.Equals and record-struct plumbing, so deleting every authored method still left
        // the array non-empty. Naming the six operations is a floor a rename or deletion breaks.
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
