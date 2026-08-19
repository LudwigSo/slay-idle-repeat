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
/// The energy ceiling the aggregate holds. These are the invariant tripwires no command can trip —
/// every handler computes legal banks via <c>EnergyMath</c> first — so <c>Apply</c> cannot express
/// the inputs; regen behaviour itself is covered at Apply by <c>BeginSessionRefillTests</c>.
/// </summary>
public sealed class PlayerEnergyTests
{
    private static ContentSnapshot Content => ProgressionDocuments.Shipped;

    private static EnergyTuning Tuning => EnergyTuning.Read(ProgressionDocuments.Shipped);

    private static Core.Model.Player At(int legendLevel, EnergyBanks banks) =>
        Core.Model.Player
            .Rehydrate(PlayerSnapshots.With(legendLevel: legendLevel, energy: banks), Content)
            .Value;

    /// <summary>Overflow into the Reserve is a movement within one currency, not a credit and a debit.</summary>
    [Fact]
    public void The_delta_counts_both_banks_as_one_currency()
    {
        var player = At(1, new EnergyBanks(120, 0));

        var moved = player.SetEnergy(new EnergyBanks(120, 40), Tuning, "ad_energy");

        moved.Delta.ShouldBe(40);
    }

    [Fact]
    public void A_spend_reports_a_negative_delta()
    {
        var player = At(1, new EnergyBanks(100, 0));

        var moved = player.SetEnergy(EnergyMath.Spend(player.Energy, 20).Banks, Tuning, "run_start");

        player.Energy.ShouldBe(new EnergyBanks(80, 0));
        moved.Delta.ShouldBe(-20);
    }

    /// <summary>120 at Legend Level 1: <c>baseMax</c> 120, and <c>MaxEnergy</c> counts levels gained.</summary>
    [Fact]
    public void The_main_bar_cannot_be_pushed_past_Max_Energy()
    {
        var player = At(1, new EnergyBanks(100, 0));

        var act = () => player.SetEnergy(new EnergyBanks(121, 0), Tuning, "impossible_grant");

        Should.Throw<InvalidOperationException>(act)
              .Message.ShouldMatchWildcard("*121 exceeds 120*the main Energy bar*10 §3*");

        player.Energy.ShouldBe(new EnergyBanks(100, 0), "a refused write changes nothing");
    }

    [Fact]
    public void Exactly_Max_Energy_is_allowed()
    {
        var player = At(1, new EnergyBanks(100, 0));

        player.SetEnergy(new EnergyBanks(120, 0), Tuning, "daily_free_refill");

        player.Energy.Energy.ShouldBe(120);
    }

    [Fact]
    public void The_reserve_cannot_be_pushed_past_its_capacity()
    {
        var player = At(1, new EnergyBanks(120, 100));

        var act = () => player.SetEnergy(new EnergyBanks(120, 121), Tuning, "impossible_overflow");

        Should.Throw<InvalidOperationException>(act)
              .Message.ShouldMatchWildcard("*the Energy Reserve*28 C2*");
    }

    /// <summary>Without this, a hard-coded 120 would satisfy every other case and "+2 per Legend Level" would be silently unimplemented.</summary>
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

    public static TheoryData<int> EveryAuthoredLegendLevel
    {
        get
        {
            var levels = new TheoryData<int>();
            for (var level = ProgressionDocuments.ShippedLegendLevelMin;
                 level <= ProgressionDocuments.ShippedLegendLevelMax;
                 level++)
            {
                levels.Add(level);
            }

            return levels;
        }
    }

    /// <summary>
    /// The formula exists twice by design — <c>Model</c> may not reference <c>Rules</c> (30 §11.4),
    /// so the aggregate cannot call <c>EnergyMath</c>. This is the compensating control: both sides
    /// must agree at every authored Legend Level.
    /// </summary>
    [Theory]
    [MemberData(nameof(EveryAuthoredLegendLevel))]
    public void The_aggregates_ceiling_is_the_same_number_EnergyMath_computes(int level)
    {
        var tuning = Tuning;
        var expected = EnergyMath.MaxEnergy(tuning, level);
        var reserve = EnergyMath.ReserveCapacity(tuning, level);

        Should.NotThrow(
            () => At(level, new EnergyBanks(0, 0)).SetEnergy(new EnergyBanks(expected, reserve), tuning, "probe"),
            $"level {level}: the aggregate must accept EnergyMath's own ({expected}, {reserve})");

        Should.Throw<InvalidOperationException>(
            () => At(level, new EnergyBanks(0, 0)).SetEnergy(new EnergyBanks(expected + 1, 0), tuning, "probe"));
    }

    /// <summary>
    /// A flat <c>&lt;= cap</c> check would make every already-full player throw on their first
    /// command after a patch that lowered <c>baseMax</c> — the case that forces the ceiling to be
    /// <c>max(cap, current)</c>.
    /// </summary>
    [Fact]
    public void A_player_left_above_the_cap_by_a_balance_patch_loads_and_drains()
    {
        // reserveMultipleOfMax 0 so the accrual genuinely has nowhere to deposit; with the shipped
        // 1x Reserve the assertion would be about overflow routing rather than the ceiling.
        var patched = EnergyTuning.Read(ProgressionDocuments.With(
            baseMax: ContentValue.Number(60),
            maxCap: ContentValue.Number(60),
            reserveMultipleOfMax: ContentValue.Number(0)));

        var player = At(1, new EnergyBanks(120, 0));

        var accrued = EnergyMath.Accrue(patched, 1, player.Energy, TimeSpan.FromHours(1));
        player.SetEnergy(accrued.Banks, patched, "energy_regen").Delta.ShouldBe(0);
        player.Energy.ShouldBe(new EnergyBanks(120, 0));

        player.SetEnergy(EnergyMath.Spend(player.Energy, 100).Banks, patched, "run_start");
        player.Energy.ShouldBe(new EnergyBanks(20, 0));

        Should.Throw<InvalidOperationException>(
            () => player.SetEnergy(new EnergyBanks(61, 0), patched, "impossible_grant"));
    }

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

    /// <summary>The anchor advances by <c>wholeUnits × interval</c>, never to the instant asked about.</summary>
    [Fact]
    public void An_accrual_writes_the_banks_and_moves_the_anchor_together()
    {
        var player = At(1, new EnergyBanks(0, 0));
        var before = player.EnergyAnchorUtc;

        var accrued = EnergyMath.Accrue(Tuning, 1, player.Energy, TimeSpan.FromMinutes(10));
        var moved = player.AccrueEnergy(accrued.Banks, accrued.AnchorAdvance, Tuning, "energy_regen");

        // Two whole 4-minute units in ten minutes: two Energy, the anchor moves 8 minutes, and the
        // remaining two minutes survive to the next command.
        accrued.AnchorAdvance.ShouldBe(TimeSpan.FromMinutes(8));
        player.Energy.ShouldBe(new EnergyBanks(2, 0));
        player.EnergyAnchorUtc.ShouldBe(before.AddMinutes(8));
        moved.Id.ShouldBe(CurrencyId.ENERGY);
        moved.Delta.ShouldBe(2);
    }

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

    [Fact]
    public void A_zero_advance_leaves_the_anchor_where_it_was()
    {
        var player = At(1, new EnergyBanks(0, 0));
        var before = player.EnergyAnchorUtc;

        player.AccrueEnergy(player.Energy, TimeSpan.Zero, Tuning, "energy_regen");

        player.EnergyAnchorUtc.ShouldBe(before);
    }
}
