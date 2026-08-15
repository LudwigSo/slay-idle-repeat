using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// The two phase boundaries, the &lt;= that "triggered at 66%" means, and the first-clear
/// extension read as a wider HP band rather than a longer fight.
/// </summary>
public sealed class BossPhaseRulesTests
{
    /// <summary>A fraction at or below a boundary is already in the lower phase.</summary>
    [Theory]
    [InlineData(1.00, 1)]
    [InlineData(0.67, 1)]
    [InlineData(0.6600, 2, "'triggered AT 66%' is <=, not <")]
    [InlineData(0.34, 2)]
    [InlineData(0.3300, 3, "and so is 33%")]
    [InlineData(0.00, 3)]
    public void PhaseFor_reads_17_1s_bands_on_a_repeat_clear(
        double hpFraction, int expected, string? because = null)
    {
        BossPhaseRules.PhaseFor(hpFraction, firstClear: false)
                      .ShouldBe(expected, because ?? "17 §1's authored bands");
    }

    /// <summary>
    /// A phase is an HP band; at constant DPS its duration is proportional to its width, so a 20%
    /// longer phase 1 widens the band and moves the boundary to 1.0 - 1.2 x (1.0 - 0.66) = 0.5920.
    /// </summary>
    [Fact]
    public void A_first_clear_widens_phase_1s_band_by_20_percent()
    {
        BossPhaseRules.Phase2HpFraction(firstClear: false).ShouldBe(
            0.66, "the control: a repeat clear is 17 §1's authored boundary");

        BossPhaseRules.Phase2HpFraction(firstClear: true).ShouldBe(
            0.5920,
            "1.0 - 1.20 x (1.0 - 0.66), rounded at 05 §1.1's four decimals — NOT a longer fight: " +
            "CombatRules.PvE.MaxTicks already equals CombatLog.MaxTicks, so there is no headroom to " +
            "extend into, and extending would lengthen the whole fight rather than phase 1");
    }

    /// <summary>
    /// The extension reaches phase 1 only: at 66% a first clear is still in phase 1, and the 33%
    /// boundary is untouched, so phase 2 absorbs the whole difference.
    /// </summary>
    [Theory]
    [InlineData(0.6600, 1)]
    [InlineData(0.5921, 1)]
    [InlineData(0.5920, 2)]
    [InlineData(0.3301, 2)]
    [InlineData(0.3300, 3)]
    public void The_first_clear_extension_moves_only_the_phase_2_boundary(double hpFraction, int expected)
    {
        BossPhaseRules.PhaseFor(hpFraction, firstClear: true).ShouldBe(expected);

        BossPhaseRules.Phase3HpFraction.ShouldBe(
            0.33, "only phase 1 is extended — 17 §1 says nothing about phase 3");
    }

    /// <summary>
    /// The widened boundary is derived from the two authored constants rather than being a fourth
    /// constant somebody could retune independently; the literal 0.5920 is pinned separately by
    /// <see cref="A_first_clear_widens_phase_1s_band_by_20_percent"/>.
    /// </summary>
    [Fact]
    public void The_widened_boundary_is_the_authored_band_times_the_authored_widening()
    {
        var widened = DeterminismRounding.Round(
            1.0 - (BossPhaseRules.FirstClearPhase1Widening *
                   (1.0 - BossPhaseRules.AuthoredPhase2HpFraction)));

        BossPhaseRules.Phase2HpFraction(firstClear: true).ShouldBe(widened);
    }

    [Fact]
    public void There_are_exactly_three_phases_and_the_log_can_name_all_of_them()
    {
        BossPhaseRules.FirstPhase.ShouldBe(1);
        BossPhaseRules.FinalPhase.ShouldBe(3);
        BossScript.PhaseCount.ShouldBe(BossPhaseRules.FinalPhase);

        CombatEventType.PhaseChange.ShouldBe(
            (CombatEventType)15, "the ordinal is a wire value inside LogHash — appended, never moved");
    }
}
