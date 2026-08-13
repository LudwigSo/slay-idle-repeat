using Shouldly;
using SlayIdleRepeat.BalanceHarness.Guardrails;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Rules;
using SlayIdleRepeat.BalanceHarness.Sweep;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// 🔴 <b>Steering S1/S2 — the discriminating controls.</b> Every guardrail here is shown to
/// <b>fire</b> on a subject that breaches it and to <b>pass</b> on one that does not.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Why this file exists at all.</b> A statistical assertion is the easiest cannot-fail test
/// there is: <em>"clear rate in [62%, 78%]"</em> passes for a great many broken simulators, and on
/// the shipped data guardrails 3 and 4 have <b>no cleared fights to evaluate</b> — so without
/// controls they would be untested code reporting on nothing. Each guardrail is probed in two
/// shapes:
/// </para>
/// <list type="number">
///   <item><b>Synthetic subjects</b> that pin the assertion's boundary exactly — one tick either
///   side of 12 s and 70 s, one fight either side of the clear-rate band. These test the
///   <em>assertion</em>.</item>
///   <item><b>Real simulations</b> of deliberately over- and under-powered builds, which must land
///   outside the band and breach the floor and the ceiling for real. These test that the
///   <em>simulator</em> can move the number the assertion reads.</item>
/// </list>
/// <para>
/// ⚠️ The real controls use <c>heroPowerMultiple</c>, which is never anything but 1.0 in the sweep
/// itself. The multiples below are measured values, not guesses: 200 × par clears 100% in 3.35 s,
/// and the slow-wall build at 8 × par clears 100% with every clear above 75 s.
/// </para>
/// </remarks>
[Collection(WallClockSensitive.Name)]
public sealed class GuardrailDiscriminationTests
{
    // ── guardrail 1 ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Guardrail_1_passes_a_cell_inside_the_authored_band()
    {
        var result = SweepGuardrails.ClearRateAtPar([SyntheticCell(clears: 70, fights: 100)], 0.62, 0.78);

        result.Verdict.ShouldBe(GuardrailVerdict.Pass);
        result.SubjectCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(62, 100, GuardrailVerdict.Pass)]   // exactly the minimum — inclusive
    [InlineData(78, 100, GuardrailVerdict.Pass)]   // exactly the maximum — inclusive
    [InlineData(61, 100, GuardrailVerdict.Fail)]   // one fight below
    [InlineData(79, 100, GuardrailVerdict.Fail)]   // one fight above
    [InlineData(0, 100, GuardrailVerdict.Fail)]
    [InlineData(100, 100, GuardrailVerdict.Fail)]
    public void Guardrail_1_discriminates_one_fight_either_side_of_the_band(
        int clears, int fights, GuardrailVerdict expected)
    {
        SweepGuardrails.ClearRateAtPar([SyntheticCell(clears, fights)], 0.62, 0.78)
            .Verdict.ShouldBe(expected);
    }

    [Fact]
    public void Guardrail_1_fires_on_a_REAL_over_powered_build_and_on_a_REAL_under_powered_one()
    {
        // 🔴 The control that shows the simulator can actually move the number. Both must FAIL, and
        // they must fail from opposite ends: an over-powered build above the band, an at-par build
        // below it.
        var overPowered = RealCell("ARCH_TANK_THORNS", multiple: 200.0, fights: 30);
        var underPowered = RealCell("ARCH_CRIT", multiple: 1.0, fights: 30);

        overPowered.ClearRate.ShouldBe(1.0);
        underPowered.ClearRate.ShouldBe(0.0);

        SweepGuardrails.ClearRateAtPar([overPowered], 0.62, 0.78).Verdict.ShouldBe(GuardrailVerdict.Fail);
        SweepGuardrails.ClearRateAtPar([underPowered], 0.62, 0.78).Verdict.ShouldBe(GuardrailVerdict.Fail);
    }

    [Fact]
    public void Guardrail_1_passes_a_REAL_build_that_sits_inside_the_band()
    {
        // 🔴 The other half of the control: a real simulated cell that is genuinely inside [62%, 78%],
        // so "guardrail 1 fails on the shipped data" is a statement about the data and not about an
        // assertion that can never pass. ARCH_LIFESTEAL at 2.4 × par measures 74.0% over 200 seeded
        // fights; the seeds are fixed, so this number is reproducible rather than sampled.
        var cell = RealCell("ARCH_LIFESTEAL", multiple: 2.4, fights: 200);

        cell.ClearRate.ShouldBeInRange(0.62, 0.78);
        SweepGuardrails.ClearRateAtPar([cell], 0.62, 0.78).Verdict.ShouldBe(GuardrailVerdict.Pass);
    }

    [Fact]
    public void Guardrail_1_over_an_empty_sweep_is_inconclusive_rather_than_a_pass()
    {
        var result = SweepGuardrails.ClearRateAtPar([], 0.62, 0.78);

        result.Verdict.ShouldBe(GuardrailVerdict.Inconclusive);
        result.Passed.ShouldBeFalse();
        result.SubjectCount.ShouldBe(0);
    }

    // ── guardrail 3 — the 12 s floor ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(241, GuardrailVerdict.Pass)]  // 12.05 s — one tick above the floor
    [InlineData(240, GuardrailVerdict.Pass)]  // 12.00 s — exactly the floor, which is not "below"
    [InlineData(239, GuardrailVerdict.Fail)]  // 11.95 s — one tick below
    [InlineData(67, GuardrailVerdict.Fail)]   //  3.35 s — the measured over-powered clear
    public void Guardrail_3_discriminates_one_tick_either_side_of_twelve_seconds(
        int ticks, GuardrailVerdict expected)
    {
        SweepGuardrails.HardFloor([SyntheticCell(clears: 10, fights: 10, clearedTicks: ticks)])
            .Verdict.ShouldBe(expected);
    }

    [Fact]
    public void Guardrail_3_fires_on_a_REAL_hugely_over_powered_build()
    {
        // 🔴 A build at 200 × par clears in about 3.35 s, well under the floor. Without this control
        // guardrail 3 would be a green tick over an empty set on the shipped data.
        var cell = RealCell("ARCH_TANK_THORNS", multiple: 200.0, fights: 30);

        cell.ClearCount.ShouldBe(30);
        cell.MedianClearedSeconds.ShouldBeLessThan(12.0);
        cell.ClearsUnderHardFloor.ShouldBe(30);

        SweepGuardrails.HardFloor([cell]).Verdict.ShouldBe(GuardrailVerdict.Fail);
    }

    [Fact]
    public void Guardrail_3_passes_a_REAL_build_that_clears_slowly_enough()
    {
        var cell = RealCell("ARCH_TANK_THORNS", multiple: 3.0, fights: 30);

        cell.ClearCount.ShouldBe(30);
        cell.MedianClearedSeconds.ShouldBeGreaterThan(12.0);

        SweepGuardrails.HardFloor([cell]).Verdict.ShouldBe(GuardrailVerdict.Pass);
    }

    // ── guardrail 4 — the 70 s ceiling ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1399, GuardrailVerdict.Pass)] // 69.95 s — one tick below the ceiling
    [InlineData(1400, GuardrailVerdict.Pass)] // 70.00 s — exactly the ceiling, which is not "above"
    [InlineData(1401, GuardrailVerdict.Fail)] // 70.05 s — one tick above
    [InlineData(1800, GuardrailVerdict.Fail)] // the 90 s cap
    public void Guardrail_4_discriminates_one_tick_either_side_of_seventy_seconds(
        int ticks, GuardrailVerdict expected)
    {
        SweepGuardrails.HardCeiling([SyntheticCell(clears: 10, fights: 10, clearedTicks: ticks)])
            .Verdict.ShouldBe(expected);
    }

    [Fact]
    public void Guardrail_4_fires_on_a_REAL_barely_winning_build()
    {
        // 🔴 A slow-wall build — ARCH_TANK_THORNS with ATK at 30% and MAX_HP at 4× — placed at 8 × par.
        // It clears every fight and every clear takes over 75 s, so the ceiling is breached for real
        // rather than by a fabricated duration.
        var cell = RealCell(SlowWall(), "SLOW_WALL", multiple: 8.0, fights: 24);

        cell.ClearCount.ShouldBe(24);
        cell.MinClearedSeconds.ShouldBeGreaterThan(70.0);
        cell.ClearsOverHardCeiling.ShouldBe(24);

        SweepGuardrails.HardCeiling([cell]).Verdict.ShouldBe(GuardrailVerdict.Fail);

        // The same cell does NOT breach the floor, which is what makes the two guardrails separate
        // assertions rather than one restated twice.
        SweepGuardrails.HardFloor([cell]).Verdict.ShouldBe(GuardrailVerdict.Pass);
    }

    [Fact]
    public void Guardrails_3_and_4_over_cells_that_cleared_nothing_are_inconclusive_rather_than_passes()
    {
        // 🔴 The exact failure mode Shouldly's ShouldAllBe has on an empty collection, and the shipped
        // data's actual state: 120 cells, zero clears. "Every clear is above 12 s" is vacuously true.
        var nothingCleared = SyntheticCell(clears: 0, fights: 50);

        var floor = SweepGuardrails.HardFloor([nothingCleared]);
        var ceiling = SweepGuardrails.HardCeiling([nothingCleared]);

        floor.Verdict.ShouldBe(GuardrailVerdict.Inconclusive);
        floor.Passed.ShouldBeFalse();
        floor.SubjectCount.ShouldBe(0);

        ceiling.Verdict.ShouldBe(GuardrailVerdict.Inconclusive);
        ceiling.Passed.ShouldBeFalse();
    }

    // ── the Sporequeen band ────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(700, GuardrailVerdict.Pass)]  // 35.00 s — the bottom of 17 §1's band
    [InlineData(1200, GuardrailVerdict.Pass)] // 60.00 s — the top
    [InlineData(699, GuardrailVerdict.Fail)]  // 34.95 s
    [InlineData(1201, GuardrailVerdict.Fail)] // 60.05 s
    public void The_Sporequeen_band_discriminates_one_tick_either_side(int ticks, GuardrailVerdict expected)
    {
        var cell = SyntheticCell(clears: 10, fights: 10, clearedTicks: ticks, bossId: "BOSS_SPOREQUEEN_VELL");

        SweepGuardrails.SporequeenBand([cell]).Verdict.ShouldBe(expected);
    }

    [Fact]
    public void The_Sporequeen_band_is_selected_by_boss_id_and_not_by_chapter_number()
    {
        // A sweep that fought the wrong script in chapter 7 must not satisfy this by accident.
        var wrongBoss = SyntheticCell(
            clears: 10, fights: 10, clearedTicks: 900, bossId: "BOSS_CINDERMAW", chapter: 7);

        var result = SweepGuardrails.SporequeenBand([wrongBoss]);

        result.Verdict.ShouldBe(GuardrailVerdict.Inconclusive);
        result.Summary.ShouldContain("not among the swept bosses");
    }

    [Fact]
    public void The_Sporequeen_band_fires_on_a_REAL_build_whose_median_falls_outside_it()
    {
        // 🔴 A real Chapter 7 fight, over-powered so that it clears and clears fast — a median far
        // below 35 s, which the band must reject.
        var cell = RealCell("ARCH_LIFESTEAL", multiple: 200.0, fights: 24, chapter: 7);

        cell.BossId.ShouldBe("BOSS_SPOREQUEEN_VELL");
        cell.ClearCount.ShouldBe(24);
        cell.MedianClearedSeconds.ShouldBeLessThan(35.0);

        SweepGuardrails.SporequeenBand([cell]).Verdict.ShouldBe(GuardrailVerdict.Fail);
    }

    // ── helpers ────────────────────────────────────────────────────────────────────────────────

    private static StatLine SlowWall()
    {
        var tank = ShippedHarness.Runner.Calibration.Archetype("ARCH_TANK_THORNS").Stats;

        return tank
            .With(Core.Content.Effects.StatId.ATK, tank[Core.Content.Effects.StatId.ATK] * 0.3)
            .With(Core.Content.Effects.StatId.MAX_HP, tank[Core.Content.Effects.StatId.MAX_HP] * 4.0);
    }

    private static CellResult RealCell(string archetypeId, double multiple, int fights, int chapter = 1) =>
        RealCell(
            ShippedHarness.Runner.Calibration.Archetype(archetypeId).Stats,
            archetypeId, multiple, fights, chapter);

    private static CellResult RealCell(
        StatLine loadout, string archetypeId, double multiple, int fights, int chapter = 1) =>
        ShippedHarness.Runner.RunCell(
            chapter, Tier.NORMAL, archetypeId, loadout, fights, heroPowerMultiple: multiple);

    /// <summary>
    /// A cell with fabricated fights, for pinning an assertion's boundary to the tick.
    /// </summary>
    /// <remarks>
    /// ⚠️ Fabricated <b>outcomes</b>, never a fabricated guardrail: the <see cref="CellResult"/> and
    /// the guardrail under test are the shipped ones, and only the fight list is constructed. That is
    /// what lets a case say "239 ticks breaches and 240 does not" without running ten thousand
    /// simulations to land on the boundary by luck.
    /// </remarks>
    private static CellResult SyntheticCell(
        int clears,
        int fights,
        int clearedTicks = 900,
        string bossId = "BOSS_THORNMAW",
        int chapter = 1)
    {
        var outcomes = new List<FightOutcome>(fights);
        for (var i = 0; i < fights; i++)
        {
            outcomes.Add(i < clears
                ? new FightOutcome(true, clearedTicks, 100.0, (ulong)i, 1.0, 3)
                : new FightOutcome(false, 200, 0.0, (ulong)i, 1.0, 1));
        }

        var hero = ShippedHarness.Runner.ParHero(
            chapter, Tier.NORMAL, ShippedHarness.Runner.Calibration.Archetypes[0].Stats);

        return new CellResult(
            chapter, Tier.NORMAL, "ARCH_SYNTHETIC", bossId,
            parPower: 1000.0, bossPower: NodePower.BossPower(1000.0), enemyLevel: 10,
            hero, outcomes);
    }
}
