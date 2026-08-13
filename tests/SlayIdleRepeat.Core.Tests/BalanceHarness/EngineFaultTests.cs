using Shouldly;
using SlayIdleRepeat.BalanceHarness.Cli;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Sweep;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// 🔴 A cell the simulator cannot run is a <b>finding</b>, not the end of the run — and it must never
/// be mistaken for a passing guardrail.
/// </summary>
/// <remarks>
/// <para>
/// This is not defensive programming for its own sake. M2-16a's sweep hit three separate engine
/// faults, each reachable only once a fight survives past boss phase 1:
/// <c>BOSS_OSSUARY_KING</c>'s phase-1 summon crashing the pre-tick (fixed in
/// <c>BattleSimulation</c>), a boss <c>PERIODIC</c> on <c>CURRENT_TARGET</c> that cannot resolve
/// (chapters 4, 6 and 8), and <c>BOSS_RIMEHOLD_P2_SHATTERBACK_FREEZE</c> being an
/// <c>APPLY_STATUS</c> with no <c>value</c> (chapter 5). A harness that died on the first of those
/// would have reported nothing about the other 119 cells.
/// </para>
/// <para>
/// 🔒 The design is deliberately asymmetric: <see cref="SweepRunner.RunCell"/> throws and
/// <see cref="SweepRunner.TryRunCell"/> reports. The sweep and the diagnostics use the reporting
/// form; the test suite and every guardrail-facing path use the throwing one, so a scaling bug
/// cannot hide as an empty cell.
/// </para>
/// </remarks>
[Collection(WallClockSensitive.Name)]
public sealed class EngineFaultTests
{
    [Fact]
    public void TryRunCell_returns_the_cell_when_the_fight_runs()
    {
        var archetype = ShippedHarness.Runner.Calibration.Archetypes[0];

        var (cell, fault) = ShippedHarness.Runner.TryRunCell(
            1, Tier.NORMAL, archetype.Id, archetype.Stats, fights: 4);

        fault.ShouldBeNull();
        cell.ShouldNotBeNull();
        cell!.FightCount.ShouldBe(4);
    }

    [Fact]
    public void TryRunCell_returns_the_fault_instead_of_throwing_when_the_engine_refuses()
    {
        // 🔴 A REAL engine fault, reproducing the shipped one rather than inventing a failure mode.
        // Deleting BOSS_GULGROT_P1_CROAK_POISON's `value` is exactly what makes
        // BOSS_RIMEHOLD_P2_SHATTERBACK_FREEZE unrunnable in chapter 5 — an APPLY_STATUS with nothing
        // to scale — except that Gulgrot's is a PHASE 1 effect, so it is reached on the first landed
        // hit instead of only by a hero strong enough to push the boss into phase 2.
        var broken = GameDataLoader.LoadWith(
            GameDataLoader.DataRoot,
            new Dictionary<string, string>(StringComparer.Ordinal) { [BossRoster.Document] = Broken() });

        var archetype = ShippedHarness.Runner.Calibration.Archetypes[0];

        var (cell, fault) = ShippedHarness.Runner.TryRunCell(
            2, Tier.NORMAL, archetype.Id, archetype.Stats, fights: 2, broken);

        cell.ShouldBeNull();
        fault.ShouldNotBeNullOrWhiteSpace();

        // 🔴 The fault names WHICH cell and WHICH rule refused. A fault string carrying only the cell
        // key would be satisfied by any failure whatsoever — an out-of-range chapter, a missing
        // archetype — and this case would stop being about the authoring hole it is named for.
        fault!.ShouldContain("C2 NORMAL ARCH_CRIT", Case.Sensitive, "which cell");
        fault.ShouldContain("BOSS_GULGROT_P1_CROAK_POISON", Case.Sensitive, "which effect");
        fault.ShouldContain("APPLY_STATUS with no value", Case.Sensitive, "which authoring rule");

        // ...and the throwing form still throws, which is what the test suite and the guardrails rely
        // on — with the SAME identity, so a cell that died of something else cannot pass as this.
        Should.Throw<ArgumentException>(() => ShippedHarness.Runner.RunCell(
                2, Tier.NORMAL, archetype.Id, archetype.Stats, fights: 2, broken))
            .Message.ShouldContain("APPLY_STATUS with no value", Case.Sensitive);
    }

    [Fact]
    public void A_faulted_sweep_reports_the_fault_and_exits_non_zero()
    {
        // 🔴 The CI contract for a fault. It is NOT graded as a balance result: the report names it in
        // its own section, and the exit code is the breach code even if every measurable guardrail
        // happened to pass.
        var output = new StringWriter();
        var dataRoot = Path.Combine(Path.GetTempPath(), $"balance-harness-data-{Guid.NewGuid():N}");

        try
        {
            CopyTree(GameDataLoader.DataRoot, dataRoot);

            File.WriteAllText(
                Path.Combine(dataRoot, BossRoster.Document.Replace('/', Path.DirectorySeparatorChar)),
                Broken());

            var exit = HarnessRun.Run(
                [
                    "assert", "--data", dataRoot, "--chapters", "2", "--tiers", "NORMAL",
                    "--archetypes", "ARCH_CRIT", "--fights", "2",
                ],
                output);

            exit.ShouldBe(HarnessRun.ExitGuardrailBreach);
            output.ToString().ShouldContain("[ENGINE FAULT]");
        }
        finally
        {
            if (Directory.Exists(dataRoot))
            {
                Directory.Delete(dataRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void A_shortfall_probe_that_faults_reports_it_rather_than_a_multiple()
    {
        // The probe raises the hero's power until the fight reaches a later boss phase, which is
        // exactly where the shipped scripts fault. It must come back carrying the fault, not a number.
        var probe = new SlayIdleRepeat.BalanceHarness.Diagnostics.ShortfallProbe(
            5, Tier.NORMAL, "ARCH_CRIT", 32.13, 0.0, 0.70, 120, "APPLY_STATUS with no value");

        probe.ToString().ShouldContain("NOT MEASURABLE");
        probe.ToString().ShouldContain("32.13");

        new SlayIdleRepeat.BalanceHarness.Diagnostics.ShortfallProbe(
                1, Tier.NORMAL, "ARCH_CRIT", 2.85, 0.767, 0.70, 120, null)
            .ToString()
            .ShouldContain("reaches 70% at 2.85 x par");
    }

    /// <summary>The shipped boss document with one authored <c>value</c> removed. See the cases above.</summary>
    private static string Broken()
    {
        var text = File.ReadAllText(Path.Combine(GameDataLoader.DataRoot, BossRoster.Document));
        var broken = text.Replace(
            "\"statusId\": \"POISON\",\n          \"value\": 0.02,",
            "\"statusId\": \"POISON\",",
            StringComparison.Ordinal);

        if (string.Equals(broken, text, StringComparison.Ordinal))
        {
            broken = text.Replace(
                "\"statusId\": \"POISON\",\r\n          \"value\": 0.02,",
                "\"statusId\": \"POISON\",",
                StringComparison.Ordinal);
        }

        // 🔒 The edit has to have MATCHED. An unmatched one would leave this case running the shipped
        // document and asserting that a healthy fight faults — a confusing red instead of a clear one.
        broken.ShouldNotBe(text);

        return broken;
    }

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);

        foreach (var directory in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(from, to, StringComparison.Ordinal));
        }

        foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(from, to, StringComparison.Ordinal), overwrite: true);
        }
    }
}
