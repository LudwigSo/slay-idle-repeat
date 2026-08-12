using Shouldly;
using SlayIdleRepeat.Core.Primitives;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `17` §1 — the two phase boundaries, the <c>&lt;=</c> that <em>"triggered at 66%"</em> means,
/// and the first-clear extension read as a wider HP band rather than a longer fight.
/// </summary>
public sealed class BossPhaseRulesTests
{
    /// <summary>
    /// 🔒 `17` §1 — <em>"exactly 3, triggered at 100%, 66% and 33% Max HP"</em>. Read as a reading:
    /// a fraction at or below a boundary is already in the lower phase.
    /// </summary>
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
    /// 🔴 `17` §1's first-clear extension: <em>"phase 1 lasts 20% longer"</em>. A phase is an HP
    /// band, and at constant DPS its duration is proportional to its width — so the band widens by
    /// 20% and the boundary moves to <c>1.0 − 1.2 × (1.0 − 0.66) = 0.5920</c>.
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
    /// 🔒 The extension reaches phase 1 <b>only</b>: at 66% a first clear is still in phase 1, and
    /// `17` §1's 33% boundary is untouched, so phase 2 absorbs the whole difference.
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
    /// The widened boundary is <b>derived</b> from `17` §1's two authored numbers rather than being a
    /// fourth constant somebody could retune independently.
    /// </summary>
    /// <remarks>
    /// 🔒 The expectation is computed here from the authored constants, so a change to either one
    /// moves both sides together and this case keeps meaning <em>"the derivation is that
    /// formula"</em> rather than <em>"the number is 0.5920"</em>. The literal 0.5920 is pinned by
    /// <see cref="A_first_clear_widens_phase_1s_band_by_20_percent"/>, which is the case that fails
    /// if the constants drift from the document.
    /// </remarks>
    [Fact]
    public void The_widened_boundary_is_the_authored_band_times_the_authored_widening()
    {
        var widened = DeterminismRounding.Round(
            1.0 - (BossPhaseRules.FirstClearPhase1Widening *
                   (1.0 - BossPhaseRules.AuthoredPhase2HpFraction)));

        BossPhaseRules.Phase2HpFraction(firstClear: true).ShouldBe(widened);
    }

    /// <summary>
    /// 🔒 `17` §1 gives every boss <b>exactly three</b> phases, and the log's <c>PhaseChange</c> can
    /// carry only <c>1</c>, <c>2</c> or <c>3</c> (<c>CombatEvent</c>'s slot contract).
    /// </summary>
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
