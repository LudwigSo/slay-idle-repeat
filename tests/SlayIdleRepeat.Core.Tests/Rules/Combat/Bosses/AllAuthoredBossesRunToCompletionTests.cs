using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 The headline regression: <em>"there is no test that runs the authored bosses to completion."</em>
/// The nine authored scripts, through the REAL engine, to `05` §3's 1800-tick bound. Four of the nine
/// used to fault with an unhandled <c>EffectContextException</c> partway through — never producing a
/// <see cref="SimulationResult"/> at all — the moment their phase reached the mechanic that names it.
/// </summary>
/// <remarks>
/// 🔒 Exit criterion ③'s run half: the zero-bespoke-code half was proved by an IL scan, and this is the
/// test for the other.
/// <para>
/// ⚠️ "Runs to completion" means a <see cref="SimulationResult"/> comes back — win, loss or timeout — not
/// that the hero wins. The hero is calibrated to bring every boss down well inside the bound, so every
/// case does win, but the assertion is on <c>DurationTicks</c> being within bounds and the fight not
/// throwing, which is the claim `05` §1's contract actually makes.
/// </para>
/// <para>
/// 🔒 All eight affected effects were <c>PERIODIC</c>s naming <c>CURRENT_TARGET</c> with no attack in
/// flight to carry one. <c>TargetResolverTests</c> pins the fix; this is the end-to-end proof that it is
/// what makes those four bosses' fights complete.
/// </para>
/// </remarks>
public sealed class AllAuthoredBossesRunToCompletionTests
{
    /// <summary>Every one of `17` §1.2's nine authored scripts.</summary>
    public static IEnumerable<object[]> AllBossIds() => RealBossFight.AllBossIds(ShippedBosses.Content);

    [Theory]
    [MemberData(nameof(AllBossIds))]
    public void An_authored_boss_runs_to_completion_without_faulting(string bossId)
    {
        var result = RealBossFight.Run(bossId, ShippedBosses.Content);

        result.DurationTicks.ShouldBeGreaterThan(
            0, "05 §3 fixes a fight at 1..1800 ticks — zero would mean nothing ran");
        result.DurationTicks.ShouldBeLessThanOrEqualTo(
            CombatLog.MaxTicks, "05 §3's 90 s / 1800-tick bound is the fight's own ceiling");
    }

    /// <summary>
    /// 🔒 The four previously-faulting bosses, individually — so a regression in just one of them
    /// names itself in the test explorer rather than hiding inside the Theory's generic case label.
    /// </summary>
    [Theory]
    [InlineData("BOSS_CINDERMAW")]
    [InlineData("BOSS_RIMEHOLD")]
    [InlineData("BOSS_COGITATOR_PRIME")]
    [InlineData("BOSS_DICELORD")]
    public void The_four_bosses_M2_R3_named_reach_phase_3_and_complete(string bossId)
    {
        var result = RealBossFight.Run(bossId, ShippedBosses.Content);

        var phaseChanges = result.Log
            .Where(e => e.Type == CombatEventType.PhaseChange)
            .Select(e => (int)e.Value)
            .ToArray();

        phaseChanges.ShouldContain(
            3, $"{bossId} must reach phase 3 for this test to actually exercise its M2-R3 mechanic " +
               "— reaching phase 3 vacuously proves nothing if the fight ended in phase 1");
    }
}
