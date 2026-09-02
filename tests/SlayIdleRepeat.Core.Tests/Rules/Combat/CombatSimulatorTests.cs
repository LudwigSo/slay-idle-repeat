using System.Diagnostics;
using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Tests.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using SlayIdleRepeat.Core.Tests.TestSupport;
using Xunit;
using Xunit.Abstractions;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>Three requirements: deterministic, engine-independent, and a full fight in under 5 ms.</summary>
[Collection(WallClockSensitive.Name)]
public sealed class CombatSimulatorTests
{
    private readonly ITestOutputHelper _output;

    public CombatSimulatorTests(ITestOutputHelper output) => _output = output;

    /// <summary>Same inputs produce a byte-identical log and <c>LogHash</c> every time.</summary>
    [Fact]
    public void The_same_inputs_produce_an_identical_log_and_LogHash()
    {
        var first = WorstCaseFight(seed: 0xDEADBEEFUL);
        var second = WorstCaseFight(seed: 0xDEADBEEFUL);

        second.LogHash.ShouldBe(first.LogHash);
        second.DurationTicks.ShouldBe(first.DurationTicks);
        second.HeroWon.ShouldBe(first.HeroWon);
        second.HeroHpRemaining.ShouldBe(first.HeroHpRemaining);
        second.Log.ShouldBe(first.Log);
    }

    /// <summary>
    /// The public signature runs a whole fight — the pre-tick, aggregation, swings through the real
    /// pipeline, and a sealed, hashed log.
    /// </summary>
    /// <remarks>
    /// The first three assertions are what make the case discriminating: all four "prescribed" ones
    /// alone would hold for a fight in which the pipeline deals no damage (always missing). Landed
    /// hits, both enemies killed, and finishing inside the cap are arithmetic rather than hope for
    /// this hero's ATK against this roster's HP.
    /// </remarks>
    [Fact]
    public void The_public_entry_point_runs_a_whole_fight()
    {
        var result = PublicFight(seed: 1);

        result.Log.ShouldContain(e => e.Type == CombatEventType.Hit);
        result.HeroWon.ShouldBeTrue();
        result.DurationTicks.ShouldBeLessThan(CombatLog.MaxTicks);

        // The roster comes FIRST and BattleStart follows all of it, which is what lets a replayer draw
        // an opening arena that already has a bar for everyone standing in it. This roster summons
        // nothing, so every spawn in the log is an opening one.
        var opening = result.Log
            .TakeWhile(e => e.Type != CombatEventType.BattleStart)
            .ToArray();

        result.Log[0].Type.ShouldBe(CombatEventType.ActorSpawned);
        opening.Count(e => e.Type == CombatEventType.ActorSpawned)
            .ShouldBe(result.Log.Count(e => e.Type == CombatEventType.ActorSpawned));
        opening.ShouldAllBe(e => e.Tick == 0);

        result.Log[^1].Type.ShouldBe(CombatEventType.BattleEnd);
        result.DurationTicks.ShouldBeInRange(1, CombatLog.MaxTicks);
        result.LogHash.ShouldNotBe(0UL);
    }

    /// <summary>
    /// <c>BattleSeams.Strict</c> still refuses attack resolution by name, and is no longer what a
    /// plan gets by default: a fight assembled on the strict set must keep failing loudly rather than
    /// running 1800 ticks of nothing.
    /// </summary>
    [Fact]
    public void A_plan_built_on_the_strict_seams_still_refuses_the_first_swing()
    {
        var refused = Should.Throw<EffectContextException>(() => CombatSimulator.Simulate(
            BattleTestBench.Plan(
                new[]
                {
                    BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 300, atk: 8), 1),
                    BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 300, atk: 6)),
                },
                seams: static _ => BattleSeams.Strict)));

        refused.Message.ShouldContain("M2-09");
        refused.Message.ShouldContain(BattleSimulation.BasicAttackSourceId);
    }

    /// <summary>A fight with no enemies has no ticks to run and is refused rather than won.</summary>
    [Fact]
    public void A_fight_with_no_enemies_is_refused() =>
        Should.Throw<ArgumentException>(() => CombatSimulator.Simulate(
            1, StatFixtures.HeroCurve().At(60), 60, Array.Empty<ActorStats>(), 10,
            StatFixtures.CombatCapsSnapshot()));

    /// <summary>
    /// A full fight must simulate fast, measured over the median of N worst-case fights. Asserted at
    /// a documented multiple of the design budget (5 ms on a target device), with the raw median
    /// printed: CI under a debug build with a coverage collector runs 3x slower ordinarily, so a test
    /// pinned at the raw budget would flake, while one at 10x still catches a ~100x regression like an
    /// accidental per-tick aggregation of every actor.
    /// </summary>
    [Fact]
    public void A_worst_case_1800_tick_fight_simulates_inside_the_budget()
    {
        const double budgetMs = 5.0;
        const double ciMultiple = 10.0;

        // The budget is for a "full 60-second fight" — 1200 ticks. The 90 s cap is reported beside
        // it because it is the longest fight the engine can be asked for.
        var sixtySeconds = Median(60 * CombatLog.TicksPerSecond);
        var ninetySeconds = Median(CombatLog.MaxTicks);

        sixtySeconds.ShouldBeLessThan(budgetMs * ciMultiple);
        ninetySeconds.ShouldBeLessThan(budgetMs * ciMultiple);

        double Median(int maxTicks)
        {
            const int fights = 25;

            // Warm the JIT: the first call pays for compiling every method in the loop, which is not
            // what the budget is about.
            for (var i = 0; i < 3; i++)
            {
                WorstCaseFight(seed: 999, maxTicks);
            }

            var samples = new double[fights];
            for (var i = 0; i < fights; i++)
            {
                var watch = Stopwatch.StartNew();
                var result = WorstCaseFight(seed: (ulong)i, maxTicks);
                watch.Stop();

                // The measurement is only worth anything if the fight really ran the full cap.
                result.DurationTicks.ShouldBe(maxTicks);
                samples[i] = watch.Elapsed.TotalMilliseconds;
            }

            Array.Sort(samples);
            var value = samples[fights / 2];

            _output.WriteLine(
                $"05 headnote budget {budgetMs.ToString("0.0", CultureInfo.InvariantCulture)} ms · " +
                $"median of {fights.ToString(CultureInfo.InvariantCulture)} worst-case " +
                $"{maxTicks.ToString(CultureInfo.InvariantCulture)}-tick " +
                $"({BattleClock.SecondsAt(maxTicks).ToString("0", CultureInfo.InvariantCulture)} s) fights = " +
                $"{value.ToString("0.000", CultureInfo.InvariantCulture)} ms " +
                $"(min {samples[0].ToString("0.000", CultureInfo.InvariantCulture)}, " +
                $"max {samples[^1].ToString("0.000", CultureInfo.InvariantCulture)})");

            return value;
        }
    }

    /// <summary>
    /// The worst authorised case: one hero, three pets, five enemies at double base ASPD, and
    /// nobody able to finish anybody — so every slot runs on every tick for the whole cap.
    /// </summary>
    private static SimulationResult WorstCaseFight(ulong seed, int maxTicks = CombatLog.MaxTicks)
    {
        var actors = new List<ActorPlan>
        {
            BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 1_000_000, aspd: 2.0), 60),
            BattleTestBench.Pet(0),
            BattleTestBench.Pet(1),
            BattleTestBench.Pet(2),
        };

        for (var i = 0; i < 5; i++)
        {
            actors.Add(BattleTestBench.Enemy(i, BattleTestBench.Stats(maxHp: 1_000_000, aspd: 2.0)));
        }

        // The real pipeline, not a recording double: the budget is about the cost of a fight, and a
        // double that subtracts one number and appends one event measures nothing.
        return CombatSimulator.Simulate(BattleTestBench.Plan(
            actors,
            rules: new CombatRules(maxTicks, OnKillTriggersFire: true),
            battleSeed: seed));
    }

    private static SimulationResult PublicFight(ulong seed) =>
        CombatSimulator.Simulate(
            seed,
            StatFixtures.HeroCurve().At(60),
            60,
            new[] { BattleTestBench.Stats(maxHp: 300, atk: 8), BattleTestBench.Stats(maxHp: 200, atk: 6) },
            10,

            // The shipped combat-caps shape plus the status catalogue, so the entry point is
            // exercised over the real content rather than three numbers a test chose.
            StatusFixtures.With(StatFixtures.CombatCapsSnapshot()));
}
