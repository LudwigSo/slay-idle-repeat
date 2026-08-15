using Shouldly;
using SlayIdleRepeat.BalanceHarness.Cli;
using SlayIdleRepeat.BalanceHarness.Content;
using SlayIdleRepeat.BalanceHarness.Model;
using SlayIdleRepeat.BalanceHarness.Sweep;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// A cell the simulator cannot run is a finding, not the end of the run, and must never be mistaken
/// for a passing guardrail. <see cref="SweepRunner.RunCell"/> throws and
/// <see cref="SweepRunner.TryRunCell"/> reports; the test suite and guardrail-facing paths use the
/// throwing form so a scaling bug cannot hide as an empty cell.
/// </summary>
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
        // A real engine fault, reproducing a shipped one rather than inventing a failure mode.
        var broken = GameDataLoader.LoadWith(
            GameDataLoader.DataRoot,
            new Dictionary<string, string>(StringComparer.Ordinal) { [BossRoster.Document] = Broken() });

        var archetype = ShippedHarness.Runner.Calibration.Archetypes[0];

        var (cell, fault) = ShippedHarness.Runner.TryRunCell(
            2, Tier.NORMAL, archetype.Id, archetype.Stats, fights: 2, broken);

        cell.ShouldBeNull();
        fault.ShouldNotBeNullOrWhiteSpace();

        // The fault names WHICH cell and WHICH rule refused, not just that something failed.
        fault!.ShouldContain("C2 NORMAL ARCH_CRIT", Case.Sensitive, "which cell");
        fault.ShouldContain("BOSS_GULGROT_P1_CROAK_POISON", Case.Sensitive, "which effect");
        fault.ShouldContain("APPLY_STATUS with no value", Case.Sensitive, "which authoring rule");

        // ...and the throwing form still throws, with the same identity.
        Should.Throw<EffectContextException>(() => ShippedHarness.Runner.RunCell(
                2, Tier.NORMAL, archetype.Id, archetype.Stats, fights: 2, broken))
            .Message.ShouldContain("APPLY_STATUS with no value", Case.Sensitive);
    }

    [Fact]
    public void A_faulted_sweep_reports_the_fault_and_exits_non_zero()
    {
        // A fault is not graded as a balance result: the exit code is the breach code even if every
        // measurable guardrail happened to pass.
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
        // A probe that reaches a faulting fight must come back carrying the fault, not a number.
        var probe = new SlayIdleRepeat.BalanceHarness.Diagnostics.ShortfallProbe(
            5, Tier.NORMAL, "ARCH_CRIT", 32.13, 0.0, 0.70, 120, "APPLY_STATUS with no value");

        probe.ToString().ShouldContain("NOT MEASURABLE");
        probe.ToString().ShouldContain("32.13");

        new SlayIdleRepeat.BalanceHarness.Diagnostics.ShortfallProbe(
                1, Tier.NORMAL, "ARCH_CRIT", 2.85, 0.767, 0.70, 120, null)
            .ToString()
            .ShouldContain("reaches 70% at 2.85 x par");
    }

    /// <summary>The shipped boss document with one authored <c>value</c> removed.</summary>
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

        // The edit has to have matched, or this case would run the unmodified document instead.
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
