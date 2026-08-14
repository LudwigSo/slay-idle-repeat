using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 M2-R3's headline regression: <em>"there is no test that runs the authored bosses to
/// completion."</em> This is that test — the nine authored `content/bosses/bosses.json` scripts, run
/// through the REAL engine (<see cref="RealBossFight"/>: the real `05` §4 attack pipeline and the
/// real `05` §5 status engine, not <see cref="BossTestBench"/>'s recording fakes) to `05` §3's 1800-
/// tick bound. Before M2-R3, four of the nine faulted with an unhandled
/// <see cref="SlayIdleRepeat.Core.Rules.Effects.EffectContextException"/> partway through — never
/// producing a <see cref="SimulationResult"/> at all — the moment their phase reached the mechanic
/// that names it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Exit criterion ③'s run half, closed.</b> M2's own exit criterion ③ —
/// <em>"all 8 bosses run from DSL data with zero bespoke code"</em> — had its zero-bespoke-code half
/// proved by an IL scan (no <c>ldstr</c> beginning <c>BOSS_</c> under <c>Core.Rules</c>) but no test
/// for the run half. This Theory is that test, over all NINE authored scripts (the eight campaign
/// bosses plus <c>BOSS_FTUE</c>).
/// </para>
/// <para>
/// ⚠️ <b>"Runs to completion" means a <see cref="SimulationResult"/> comes back — win, loss, or
/// timeout — not that the hero wins.</b> <see cref="RealBossFight.Hero"/> is calibrated to survive
/// comfortably and bring every boss down through phase 3 well inside the 90 s bound (see the M2-R3
/// completion report for the phase-change ticks each boss produced), so every case here does win —
/// but the assertion below is on <see cref="SimulationResult.DurationTicks"/> being within bounds and
/// the fight not throwing, which is the actual claim `05` §1's <c>Simulate</c> contract makes, not on
/// who won.
/// </para>
/// <para>
/// 🔒 <b>The red state this is a green proof of, verbatim, for the four affected bosses' eight
/// effects (M2-R3's own table):</b> <c>BOSS_CINDERMAW</c>'s <c>P2_MAGMA_VENT</c>/<c>P2_VENT_REFRESH</c>
/// and <c>P3_MAGMA_VENT</c>/<c>P3_VENT_REFRESH</c>, <c>BOSS_RIMEHOLD</c>'s <c>P3_COLLAPSE</c>,
/// <c>BOSS_COGITATOR_PRIME</c>'s <c>P2_RECALIBRATE</c> and <c>P3_PISTON_SLAM</c>, and
/// <c>BOSS_DICELORD</c>'s <c>P3_ALL_IN</c> — every one a <c>PERIODIC</c> effect naming
/// <c>CURRENT_TARGET</c> with no attack in flight to carry one. See
/// <see cref="Targeting.TargetResolverTests"/> for the unit-level pin of the fix itself
/// (<c>TargetResolver.EnemyHolderHero</c>); this is the end-to-end proof that the fix is what makes
/// these four bosses' fights actually complete.
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
