using Shouldly;
using SlayIdleRepeat.BalanceHarness.Cli;
using SlayIdleRepeat.BalanceHarness.Model;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.BalanceHarness;

/// <summary>
/// 🔒 `21` §10's CLI — the three commands, every option, and the exit codes CI keys on.
/// </summary>
[Collection(WallClockSensitive.Name)]
public sealed class HarnessCliTests
{
    [Fact]
    public void No_arguments_is_the_full_sweep_at_05_9_s_ten_thousand_fights()
    {
        var options = HarnessOptions.Parse([], out var error);

        error.ShouldBeNull();
        options.ShouldNotBeNull();
        options!.Command.ShouldBe(HarnessCommand.Sweep);
        options.Fights.ShouldBe(10_000);
        options.Experiment.ShouldBe(ExperimentSelection.All);
    }

    [Fact]
    public void Assert_and_fast_carry_the_shapes_21_10_describes()
    {
        var assertOptions = HarnessOptions.Parse(["assert"], out _)!;
        assertOptions.Command.ShouldBe(HarnessCommand.Assert);
        assertOptions.Fights.ShouldBe(10_000);
        assertOptions.Experiment.ShouldBe(ExperimentSelection.None);

        var fastOptions = HarnessOptions.Parse(["fast"], out _)!;
        fastOptions.Command.ShouldBe(HarnessCommand.Fast);
        fastOptions.Fights.ShouldBe(HarnessOptions.FastFights);
        fastOptions.Experiment.ShouldBe(ExperimentSelection.None);
    }

    [Fact]
    public void Every_documented_option_is_parsed()
    {
        var options = HarnessOptions.Parse(
            [
                "sweep",
                "--fights", "250",
                "--data", "/somewhere/game-data",
                "--out", "report.txt",
                "--chapters", "1,4,7",
                "--tiers", "normal,MYTHIC",
                "--archetypes", "ARCH_CRIT, ARCH_PET",
                "--experiment", "rage",
                "--parallel", "3",
            ],
            out var error)!;

        error.ShouldBeNull();
        options.Fights.ShouldBe(250);
        options.DataRoot.ShouldBe("/somewhere/game-data");
        options.OutputPath.ShouldBe("report.txt");
        options.Chapters.ShouldBe(new[] { 1, 4, 7 });
        options.Tiers.ShouldBe(new[] { Tier.NORMAL, Tier.MYTHIC });
        options.Archetypes.ShouldBe(new[] { "ARCH_CRIT", "ARCH_PET" });
        options.Experiment.ShouldBe(ExperimentSelection.Rage);
        options.Parallelism.ShouldBe(3);
    }

    [Theory]
    [InlineData("--nonsense", "value")]
    [InlineData("swep")]
    [InlineData("--fights", "zero")]
    [InlineData("--fights", "0")]
    [InlineData("--fights", "-5")]
    [InlineData("--tiers", "LEGENDARY")]
    [InlineData("--chapters", "one")]
    [InlineData("--experiment", "perks")]
    [InlineData("--parallel", "0")]
    [InlineData("--fights")]
    public void An_argument_that_is_not_understood_is_an_error_and_never_a_silent_default(
        params string[] args)
    {
        // 🔒 A nightly job invoked with a mistyped --fights that silently ran the default would report
        // a number nobody asked for, under a name that says it is something else.
        var options = HarnessOptions.Parse(args, out var error);

        options.ShouldBeNull();
        error.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void An_unknown_argument_prints_the_usage_and_exits_two()
    {
        var output = new StringWriter();

        HarnessRun.Run(["--nonsense", "x"], output).ShouldBe(HarnessRun.ExitBadArguments);

        HarnessRun.ExitBadArguments.ShouldBe(2);
        output.ToString().ShouldContain("BalanceHarness [command] [options]");
        output.ToString().ShouldContain("is not an option this harness understands");
    }

    [Fact]
    public void Help_prints_the_usage_and_exits_zero()
    {
        var output = new StringWriter();

        HarnessRun.Run(["--help"], output).ShouldBe(HarnessRun.ExitSuccess);
        output.ToString().ShouldContain("sweep");
        output.ToString().ShouldContain("assert");
        output.ToString().ShouldContain("fast");
    }

    [Fact]
    public void Assert_exits_NON_ZERO_on_the_shipped_data_because_guardrail_1_really_does_breach()
    {
        // 🔴 The CI contract, and the single most important assertion in this file. On the shipped
        // data guardrail 1 breaches — the par build dies to every boss — and `assert` MUST fail. A
        // change that softened the band, scaled a build or excluded a cell to make the nightly job
        // green would turn this red, which is the point.
        var output = new StringWriter();

        var exit = HarnessRun.Run(
            ["assert", "--chapters", "1", "--tiers", "NORMAL", "--archetypes", "ARCH_CRIT", "--fights", "8"],
            output);

        exit.ShouldBe(HarnessRun.ExitGuardrailBreach);
        HarnessRun.ExitGuardrailBreach.ShouldBe(1);

        var text = output.ToString();

        // 🔴 GUARDRAIL 1's OWN LINE, not "somewhere in the report there is a FAIL". Guardrails 5 and 6
        // also breach on the shipped data, so `[FAIL` and `guardrail 1` asserted separately are both
        // satisfied by a report in which guardrail 1 PASSED and only its neighbours failed — which is
        // exactly what happens if the band check is neutered. Measured: with
        // `SweepGuardrails.ClearRateAtPar`'s breach test forced to false, this case stayed green while
        // every one of guardrail 1's own discrimination cases went red.
        //
        // The summary below is a breaching guardrail 1's and nothing else's: the breach count, the
        // authored band, and the clear rate that produced it.
        text.ShouldContain(
            "1/1 cells outside [62.00%, 78.00%]", Case.Sensitive,
            "guardrail 1's own breach count over its own authored band");
        text.ShouldContain(
            "lowest 0.00% at C1 NORMAL ARCH_CRIT", Case.Sensitive,
            "the par build clears nothing — the measured cause of the breach");
        text.ShouldContain("] guardrail 1: Clear rate at par", Case.Sensitive, "the guardrail 1 line");
        text.ShouldContain("[FAIL", Case.Sensitive);
    }

    [Fact]
    public void The_scope_line_states_the_cells_and_the_fight_count_that_were_actually_run()
    {
        // Steering S3 — a run that silently swept nothing must be visible in its own output.
        var output = new StringWriter();

        HarnessRun.Run(
            ["assert", "--chapters", "1,2", "--tiers", "NORMAL", "--archetypes", "ARCH_CRIT", "--fights", "6"],
            output);

        output.ToString().ShouldContain("2 chapters x 1 tiers x 1 archetypes = 2 cells x 6 fights");
        output.ToString().ShouldContain("12 fights");
    }

    [Fact]
    public void The_report_is_written_to_the_out_file_as_well_as_to_the_writer()
    {
        var path = Path.Combine(Path.GetTempPath(), $"balance-harness-{Guid.NewGuid():N}.txt");
        var output = new StringWriter();

        try
        {
            HarnessRun.Run(
                [
                    "assert", "--chapters", "1", "--tiers", "NORMAL",
                    "--archetypes", "ARCH_CRIT", "--fights", "4", "--out", path,
                ],
                output);

            File.Exists(path).ShouldBeTrue();
            File.ReadAllText(path).ShouldBe(output.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
