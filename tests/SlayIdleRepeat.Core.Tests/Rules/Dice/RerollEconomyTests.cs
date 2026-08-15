using Shouldly;
using SlayIdleRepeat.Core.Rules.Dice;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Dice;

/// <summary>`04` §3's reroll-charge and Nudge arithmetic.</summary>
public sealed class RerollEconomyTests
{
    [Fact]
    public void Base_stage_with_no_bonuses_is_one_charge()
    {
        RerollEconomy.TotalCharges(talentBonus: 0, campfireVisited: false, perkBonus: 0, rerollTokensUsed: 0)
            .ShouldBe(1);
    }

    [Fact]
    public void Every_bonus_source_adds()
    {
        // Kept under RerollEconomy.MaxStoredCharges so this asserts addition, not the separately
        // tested cap.
        RerollEconomy.TotalCharges(talentBonus: 1, campfireVisited: true, perkBonus: 0, rerollTokensUsed: 0)
            .ShouldBe(RerollEconomy.BaseChargesPerStage + 1 + RerollEconomy.CampfireBonus);
    }

    [Fact]
    public void The_total_never_exceeds_the_stored_cap()
    {
        RerollEconomy.TotalCharges(talentBonus: 2, campfireVisited: true, perkBonus: 10, rerollTokensUsed: 10)
            .ShouldBe(RerollEconomy.MaxStoredCharges);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    public void A_talent_bonus_outside_zero_to_two_is_rejected(int bonus)
    {
        Should.Throw<ArgumentOutOfRangeException>(
            () => RerollEconomy.TotalCharges(bonus, false, 0, 0));
    }

    [Fact]
    public void A_negative_perk_bonus_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RerollEconomy.TotalCharges(0, false, -1, 0));
    }

    [Fact]
    public void A_negative_token_use_count_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RerollEconomy.TotalCharges(0, false, 0, -1));
    }

    [Fact]
    public void Affordable_below_the_total()
    {
        RerollEconomy.CanAffordReroll(chargesSpentThisStage: 0, totalCharges: 1).ShouldBeTrue();
    }

    [Fact]
    public void Not_affordable_once_every_charge_is_spent()
    {
        RerollEconomy.CanAffordReroll(chargesSpentThisStage: 1, totalCharges: 1).ShouldBeFalse();
    }

    [Fact]
    public void Not_affordable_past_the_total_either()
    {
        RerollEconomy.CanAffordReroll(chargesSpentThisStage: 5, totalCharges: 1).ShouldBeFalse();
    }

    [Theory]
    [InlineData(3, 1, 4)]
    [InlineData(3, -1, 2)]
    [InlineData(6, 1, 6)]
    [InlineData(1, -1, 1)]
    public void Nudge_moves_one_step_and_clamps_to_one_through_six(int pips, int direction, int expected)
    {
        RerollEconomy.NudgePip(pips, direction).ShouldBe(expected);
    }

    [Fact]
    public void Nudge_refuses_a_direction_that_is_not_plus_or_minus_one()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RerollEconomy.NudgePip(3, 2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void Nudge_refuses_pips_outside_one_to_six(int pips)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => RerollEconomy.NudgePip(pips, 1));
    }
}
