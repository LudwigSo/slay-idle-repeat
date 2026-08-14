using System.Diagnostics;
using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Tests.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using SlayIdleRepeat.Core.Tests.BalanceHarness;
using Xunit;
using Xunit.Abstractions;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05`'s three headnote requirements: deterministic, engine-independent, and a full fight in
/// under 5 ms.
/// </summary>
[Collection(WallClockSensitive.Name)]
public sealed class CombatSimulatorTests
{
    private readonly ITestOutputHelper _output;

    public CombatSimulatorTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// 🔒 `05` headnote — <em>"<c>Simulate(seed, heroSnapshot, enemySnapshot)</c> returns an identical
    /// result every time"</em>. Same inputs, byte-identical log and <c>LogHash</c>.
    /// </summary>
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
    /// 🔒 `05` §1's public signature runs a whole fight — the pre-tick, `18` §8's aggregation, slot 4's
    /// swings through §4's pipeline, and a sealed, hashed log.
    /// </summary>
    /// <remarks>
    /// The refusal this replaced still exists and is still reachable: <c>BattleSeams.Strict</c> keeps the
    /// unwired pipeline, and <see cref="A_plan_built_on_the_strict_seams_still_refuses_the_first_swing"/>
    /// is what stops that shape from rotting now it is no longer the default.
    /// </remarks>
    /// <remarks>
    /// ⚠️ The last four assertions are the prescribed block; the first three are what make the case
    /// discriminating. All four prescribed ones hold for a fight in which the pipeline deals no damage —
    /// a <c>ResolveAttack</c> always returning <c>Missed: true</c> passes them verbatim. So the fight is
    /// asserted to have <em>landed hits</em>, <em>killed both enemies</em> and <em>finished inside the
    /// cap</em>: a Legend-60 hero swings ATK 390 into 300 and 200 HP, so all three are arithmetic rather
    /// than hope.
    /// </remarks>
    [Fact]
    public void The_public_entry_point_runs_a_whole_fight()
    {
        var result = PublicFight(seed: 1);

        result.Log.ShouldContain(e => e.Type == CombatEventType.Hit);
        result.HeroWon.ShouldBeTrue();
        result.DurationTicks.ShouldBeLessThan(CombatLog.MaxTicks);

        result.Log[0].Type.ShouldBe(CombatEventType.BattleStart);
        result.Log[^1].Type.ShouldBe(CombatEventType.BattleEnd);
        result.DurationTicks.ShouldBeInRange(1, CombatLog.MaxTicks);
        result.LogHash.ShouldNotBe(0UL);
    }

    /// <summary>
    /// 🔒 <c>BattleSeams.Strict</c> still refuses `05` §4 by name, and is no longer what a plan gets
    /// by default.
    /// </summary>
    /// <remarks>
    /// The S6 shape M2-08 built survives its own expiry. <c>UnwiredAttackPipeline</c> is still
    /// <c>EffectOpSeams.Strict</c>'s default for op resolution outside a battle, where there is no
    /// <c>BattleServices</c> to build a real pipeline from — so a fight assembled on the strict set
    /// must keep failing loudly rather than running 1800 ticks of nothing.
    /// </remarks>
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
    /// 🔒 `05`'s headnote — <em>"a full 60-second fight must simulate in &lt; 5 ms"</em>, over the
    /// <b>median</b> of N worst-case fights: 1800 ticks, five enemies, three pets, nobody dying.
    /// </summary>
    /// <remarks>
    /// ⚠️ Asserted at a documented multiple of the budget, with the raw median printed. The design number
    /// is 5 ms on a target device; this runs on shared CI under a debug build with a coverage collector,
    /// where a 3× spread is ordinary. A test pinned at 5 ms would flake and be deleted; one at 10×
    /// catches the regression that matters — an accidental per-tick aggregation of every actor, roughly
    /// 100× — and never flakes.
    /// </remarks>
    [Fact]
    public void A_worst_case_1800_tick_fight_simulates_inside_the_budget()
    {
        const double budgetMs = 5.0;
        const double ciMultiple = 10.0;

        // 🔒 `05`'s headnote budget is for a "full 60-second fight" — 1200 ticks — so that is the
        // measurement the sentence is about. The 90 s cap is reported beside it because it is the
        // longest fight the engine can be asked for and is what a boss stall actually costs.
        var sixtySeconds = Median(60 * CombatLog.TicksPerSecond);
        var ninetySeconds = Median(CombatLog.MaxTicks);

        sixtySeconds.ShouldBeLessThan(budgetMs * ciMultiple);
        ninetySeconds.ShouldBeLessThan(budgetMs * ciMultiple);

        double Median(int maxTicks)
        {
            const int fights = 25;

            // Warm the JIT: the first call pays for compiling every method in the loop, which is not
            // what `05`'s budget is about.
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
    /// The worst case `05` §3 authorises: one hero, three pets, five enemies at double base ASPD, and
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

        // 🔒 The REAL `05` §4 pipeline, since M2-09. `05`'s headnote budget is about the cost of a
        // fight, and a recording double that subtracts one number and appends one event measures
        // nothing: the per-swing cost is now three RNG draws, a ward-pool sort, a thorns sort and
        // eight roundings, across six attackers and 1800 ticks. Over the double this case would have
        // stayed green through an arbitrarily slow pipeline.
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

            // 🔒 A `content/combat_caps.json` of the SHIPPED shape — the same fixture
            // CombatCapsTests reads, so the entry point is exercised over the real pointer set
            // rather than over three numbers a test chose — plus `05` §5's catalogue, which the
            // overload now reads because it composes a WIRED StatusTimeline. See its remarks.
            StatusFixtures.With(StatFixtures.CombatCapsSnapshot()));
}
