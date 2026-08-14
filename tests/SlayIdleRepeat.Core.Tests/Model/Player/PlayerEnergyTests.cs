using Shouldly;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Model;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Economy;
using SlayIdleRepeat.Core.Tests.Content;
using SlayIdleRepeat.TestSupport;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model;

/// <summary>
/// 🔒 `30` §11.5 — <em>"Energy never exceeds max + reserve"</em>, the one invariant of the five
/// that is about Energy, held by the aggregate rather than computed by it.
/// </summary>
public sealed class PlayerEnergyTests
{
    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    private static EnergyTuning Tuning => EnergyTuning.Read(ProgressionDocuments.Shipped);

    private static Core.Model.Player At(int legendLevel, EnergyBanks banks) =>
        Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(legendLevel: legendLevel, energy: banks), Content)
            .Value;

    /// <summary>Writing banks a rule computed moves the state and attributes the movement.</summary>
    [Fact]
    public void Setting_the_banks_moves_the_state_and_emits_CurrencyChanged()
    {
        var player = At(1, new EnergyBanks(20, 0));

        var moved = player.SetEnergy(new EnergyBanks(60, 0), Tuning, "energy_regen");

        player.Energy.ShouldBe(new EnergyBanks(60, 0));
        moved.Id.ShouldBe(CurrencyId.ENERGY);
        moved.Delta.ShouldBe(40);
        moved.Reason.ShouldBe("energy_regen");
    }

    /// <summary>
    /// 🔒 The delta is the change in the two banks <b>together</b>: overflow into the Reserve is a
    /// movement within one currency, so `21` §8.3 sees one row rather than a credit and a debit.
    /// </summary>
    [Fact]
    public void The_delta_counts_both_banks_as_one_currency()
    {
        var player = At(1, new EnergyBanks(120, 0));

        var moved = player.SetEnergy(new EnergyBanks(120, 40), Tuning, "ad_energy");

        moved.Delta.ShouldBe(40);
    }

    /// <summary>A spend is the same seam, and reports a negative delta.</summary>
    [Fact]
    public void A_spend_reports_a_negative_delta()
    {
        var player = At(1, new EnergyBanks(100, 0));

        var moved = player.SetEnergy(EnergyMath.Spend(player.Energy, 20).Banks, Tuning, "run_start");

        player.Energy.ShouldBe(new EnergyBanks(80, 0));
        moved.Delta.ShouldBe(-20);
    }

    /// <summary>
    /// 🔒 `30` §11.5 — the main bar may not be pushed past Max Energy. 120 at Legend Level 1
    /// (`10` §3: <c>baseMax</c> 120, and <c>MaxEnergy</c> counts levels <em>gained</em>).
    /// </summary>
    [Fact]
    public void The_main_bar_cannot_be_pushed_past_Max_Energy()
    {
        var player = At(1, new EnergyBanks(100, 0));

        var act = () => player.SetEnergy(new EnergyBanks(121, 0), Tuning, "impossible_grant");

        Should.Throw<InvalidOperationException>(act)
              .Message.ShouldMatchWildcard("*121 exceeds 120*the main Energy bar*10 §3*");

        player.Energy.ShouldBe(new EnergyBanks(100, 0), "a refused write changes nothing");
    }

    /// <summary>Exactly Max Energy is allowed — the ceiling is inclusive, not off by one.</summary>
    [Fact]
    public void Exactly_Max_Energy_is_allowed()
    {
        var player = At(1, new EnergyBanks(100, 0));

        player.SetEnergy(new EnergyBanks(120, 0), Tuning, "daily_free_refill");

        player.Energy.Energy.ShouldBe(120);
    }

    /// <summary>🔒 `28` C2 — and the Reserve may not be pushed past its own capacity either.</summary>
    [Fact]
    public void The_reserve_cannot_be_pushed_past_its_capacity()
    {
        var player = At(1, new EnergyBanks(120, 100));

        var act = () => player.SetEnergy(new EnergyBanks(120, 121), Tuning, "impossible_overflow");

        Should.Throw<InvalidOperationException>(act)
              .Message.ShouldMatchWildcard("*the Energy Reserve*28 C2*");
    }

    /// <summary>
    /// 🔒 The ceiling grows with Legend Level, so it is genuinely derived from the player rather
    /// than from a constant: 122 at Legend Level 2, and 200 at the cap.
    /// </summary>
    /// <remarks>
    /// Without this, a hard-coded <c>120</c> would satisfy every other assertion in this file at
    /// Legend Level 1 — and `10` §3's "+2 per Legend Level" would be silently unimplemented.
    /// </remarks>
    [Theory]
    [InlineData(1, 120)]
    [InlineData(2, 122)]
    [InlineData(41, 200)]
    [InlineData(200, 200)]
    public void The_ceiling_follows_the_players_Legend_Level(int legendLevel, int maxEnergy)
    {
        var player = At(legendLevel, new EnergyBanks(0, 0));

        player.SetEnergy(new EnergyBanks(maxEnergy, 0), Tuning, "daily_free_refill");
        player.Energy.Energy.ShouldBe(maxEnergy);

        var overshoot = At(legendLevel, new EnergyBanks(0, 0));
        Should.Throw<InvalidOperationException>(
            () => overshoot.SetEnergy(new EnergyBanks(maxEnergy + 1, 0), Tuning, "impossible_grant"));
    }

    /// <summary>
    /// 🔒 The ceiling the aggregate enforces is <b>the same number</b> <c>EnergyMath</c> computes,
    /// across the whole authored Legend Level range.
    /// </summary>
    /// <remarks>
    /// ⚠️ The formula genuinely exists twice: `30` §11.4 forbids <c>Model</c> referencing <c>Rules</c>,
    /// so the aggregate cannot call <c>EnergyMath.MaxEnergy</c>. This is the compensating control — both
    /// sides over all 200 levels, so a change to one that is not mirrored is a red build rather than a
    /// rule silently accepting a bank the math would never have produced.
    /// </remarks>
    [Fact]
    public void The_aggregates_ceiling_is_the_same_number_EnergyMath_computes()
    {
        var tuning = Tuning;
        var disagreements = new List<string>();
        var probed = 0;

        for (var level = ProgressionDocuments.ShippedLegendLevelMin;
             level <= ProgressionDocuments.ShippedLegendLevelMax;
             level++)
        {
            probed++;

            var expected = EnergyMath.MaxEnergy(tuning, level);
            var reserve = EnergyMath.ReserveCapacity(tuning, level);
            var player = At(level, new EnergyBanks(0, 0));

            // The aggregate accepts exactly the maximum, and refuses one more. Two probes rather
            // than reading a private member, so what is compared is the BEHAVIOUR the invariant is.
            try
            {
                player.SetEnergy(new EnergyBanks(expected, reserve), tuning, "probe");
            }
            catch (InvalidOperationException e)
            {
                disagreements.Add($"level {level}: refused EnergyMath's own ({expected}, {reserve}) — {e.Message}");
                continue;
            }

            var overshoot = At(level, new EnergyBanks(0, 0));
            try
            {
                overshoot.SetEnergy(new EnergyBanks(expected + 1, 0), tuning, "probe");
                disagreements.Add($"level {level}: accepted {expected + 1}, above EnergyMath's max of {expected}");
            }
            catch (InvalidOperationException)
            {
                // Expected.
            }
        }

        disagreements.ShouldBeEmpty(
            "Model may not reference Rules (30 §11.4), so the aggregate cannot call EnergyMath to " +
            "learn the ceiling it must hold. Both read EnergyTuning.MaxEnergyAt; this is what keeps " +
            "that true rather than assumed.");

        // 🔒 A floor on the loop itself: if the two authored bounds ever cross or collapse, the body
        // runs zero times and the emptiness above is vacuous — S3 inside a test.
        probed.ShouldBe(
            ProgressionDocuments.ShippedLegendLevelMax - ProgressionDocuments.ShippedLegendLevelMin + 1,
            "every authored Legend Level must actually be probed");
    }

    /// <summary>
    /// 🔒 A player left above the cap by a balance patch still loads, still writes back, and drains
    /// by playing — the case that forces the ceiling to be <c>max(cap, current)</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is the assertion that would fail under the obvious reading of `30` §11.5. A flat
    /// <c>&lt;= cap</c> check would make every already-full player throw on their first command
    /// after a patch that lowered <c>baseMax</c> — a tuning change becoming an account outage —
    /// and <c>EnergyMath.Deposit</c>'s own remarks say that state is legitimate and self-correcting.
    /// </remarks>
    [Fact]
    public void A_player_left_above_the_cap_by_a_balance_patch_loads_and_drains()
    {
        // reserveMultipleOfMax 0 as well as a lowered cap, so the accrual below genuinely has
        // nowhere to put a point: with the shipped 1x Reserve it would deposit into the Reserve
        // and the assertion would be about overflow routing rather than about the ceiling.
        var patched = EnergyTuning.Read(ProgressionDocuments.With(
            baseMax: ContentValue.Number(60),
            maxCap: ContentValue.Number(60),
            reserveMultipleOfMax: ContentValue.Number(0)));

        // 120/0 was legal before the patch; the row still loads, because Rehydrate does not check
        // the ceiling at all.
        var player = At(1, new EnergyBanks(120, 0));

        // An accrual that can deposit nothing writes the same banks back rather than throwing.
        var accrued = EnergyMath.Accrue(patched, 1, player.Energy, TimeSpan.FromHours(1));
        player.SetEnergy(accrued.Banks, patched, "energy_regen").Delta.ShouldBe(0);
        player.Energy.ShouldBe(new EnergyBanks(120, 0));

        // Spending drains it, and once under the new cap the new cap is what binds.
        player.SetEnergy(EnergyMath.Spend(player.Energy, 100).Banks, patched, "run_start");
        player.Energy.ShouldBe(new EnergyBanks(20, 0));

        Should.Throw<InvalidOperationException>(
            () => player.SetEnergy(new EnergyBanks(61, 0), patched, "impossible_grant"));
    }

    /// <summary>…and nothing may push an over-cap bank <b>further</b> past the cap.</summary>
    [Fact]
    public void An_over_cap_bank_may_not_be_pushed_further()
    {
        var patched = EnergyTuning.Read(ProgressionDocuments.With(
            baseMax: ContentValue.Number(60),
            maxCap: ContentValue.Number(60),
            reserveMultipleOfMax: ContentValue.Number(0)));

        var player = At(1, new EnergyBanks(120, 0));

        Should.Throw<InvalidOperationException>(
                  () => player.SetEnergy(new EnergyBanks(121, 0), patched, "impossible_grant"))
              .Message.ShouldMatchWildcard("*121 exceeds 120*");
    }

    /// <summary>The tuning argument is required; the ceiling cannot be derived without it.</summary>
    [Fact]
    public void Setting_the_banks_without_tuning_is_refused()
    {
        Should.Throw<ArgumentNullException>(
            () => At(1, new EnergyBanks(0, 0)).SetEnergy(new EnergyBanks(1, 0), null!, "x"));
    }

    /// <summary>
    /// 🔒 An accrual writes the banks and moves the anchor in <b>one</b> call — recorded assumption
    /// <b>A1</b>: <c>wholeUnits × interval</c>, never to the instant asked about.
    /// </summary>
    [Fact]
    public void An_accrual_writes_the_banks_and_moves_the_anchor_together()
    {
        var player = At(1, new EnergyBanks(0, 0));
        var before = player.EnergyAnchorUtc;

        var accrued = EnergyMath.Accrue(Tuning, 1, player.Energy, TimeSpan.FromMinutes(10));
        var moved = player.AccrueEnergy(accrued.Banks, accrued.AnchorAdvance, Tuning, "energy_regen");

        // Two whole 4-minute units in ten minutes: two Energy, the anchor moves 8 minutes, and the
        // remaining two survive to the next command — which is the whole point of A1.
        accrued.AnchorAdvance.ShouldBe(TimeSpan.FromMinutes(8));
        player.Energy.ShouldBe(new EnergyBanks(2, 0));
        player.EnergyAnchorUtc.ShouldBe(before.AddMinutes(8));
        moved.Id.ShouldBe(CurrencyId.ENERGY);
        moved.Delta.ShouldBe(2);
    }

    /// <summary>
    /// 🔒 There is <b>no</b> way to move the anchor without writing the banks it accrued. Two
    /// internal mutators would each be individually legal, and a caller that wrote the banks and
    /// forgot the anchor would re-grant the same span on every later command — unbounded Energy
    /// that no aggregate-level invariant could see.
    /// </summary>
    [Fact]
    public void The_anchor_is_unreachable_except_through_an_accrual()
    {
        typeof(Core.Model.Player)
            .GetMethods(System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic)
            .Where(m => !m.IsPrivate)
            .Select(m => m.Name)
            .ShouldNotContain(
                "AdvanceEnergyAnchor",
                "the anchor and the banks are one fact; exposing the anchor on its own is what " +
                "makes re-granting a span representable.");
    }

    /// <summary>The anchor only moves forwards; a negative advance would pay the same span twice.</summary>
    [Fact]
    public void An_accrual_with_a_negative_anchor_advance_is_refused()
    {
        var player = At(1, new EnergyBanks(0, 0));

        Should.Throw<ArgumentOutOfRangeException>(
                  () => player.AccrueEnergy(
                      new EnergyBanks(5, 0), TimeSpan.FromSeconds(-1), Tuning, "energy_regen"))
              .Message.ShouldMatchWildcard("*only moves forwards*");

        player.Energy.ShouldBe(
            new EnergyBanks(0, 0),
            "the anchor is checked before the banks are written, so a refused accrual changes nothing");
    }

    /// <summary>A zero advance is legal and leaves the anchor alone — less than one interval passed.</summary>
    [Fact]
    public void A_zero_advance_leaves_the_anchor_where_it_was()
    {
        var player = At(1, new EnergyBanks(0, 0));
        var before = player.EnergyAnchorUtc;

        player.AccrueEnergy(player.Energy, TimeSpan.Zero, Tuning, "energy_regen");

        player.EnergyAnchorUtc.ShouldBe(before);
    }
}
