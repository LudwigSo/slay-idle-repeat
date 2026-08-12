using System.Diagnostics;
using System.Globalization;
using Shouldly;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;
using Xunit;
using Xunit.Abstractions;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05`'s three headnote requirements: deterministic, engine-independent, and a full fight in
/// under 5 ms.
/// </summary>
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
    /// 🔒 `05` §1's public signature is wired to the loop end to end, and the <b>only</b> thing
    /// standing between it and a fight is `05` §4's damage pipeline — which is M2-09's.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>THIS TEST IS AN EXPIRY, and M2-09 is the task that trips it.</b> The refusal comes from
    /// <c>UnwiredAttackPipeline</c> through <c>BattleSeams.Strict</c>, which is the S6 shape: a fight
    /// that cannot resolve a hit fails loudly rather than running 1800 ticks of nothing and reporting
    /// a timeout. The moment <c>BattleSeams.Strict.Attack</c> becomes a real pipeline this case goes
    /// red, and whoever landed it replaces it with the assertions in the comment below — which are
    /// the ones this test would make today if it could.
    /// <code>
    /// var result = PublicFight(seed: 1);
    /// result.Log[0].Type.ShouldBe(CombatEventType.BattleStart);
    /// result.Log[^1].Type.ShouldBe(CombatEventType.BattleEnd);
    /// result.DurationTicks.ShouldBeInRange(1, CombatLog.MaxTicks);
    /// result.LogHash.ShouldNotBe(0UL);
    /// </code>
    /// Everything before the hit is already exercised: the pre-tick ran, `18` §8 aggregated both
    /// blocks, slot 4 chose a target and called the seam with `05` §4's base multiplier of 1.0.
    /// </remarks>
    [Fact]
    public void The_public_entry_point_reaches_slot_4_and_stops_at_the_M2_09_seam()
    {
        var refused = Should.Throw<EffectContextException>(() => PublicFight(seed: 1));

        refused.Message.ShouldContain("M2-09");
        refused.Message.ShouldContain(BattleSimulation.BasicAttackSourceId);
    }

    /// <summary>A fight with no enemies has no ticks to run and is refused rather than won.</summary>
    [Fact]
    public void A_fight_with_no_enemies_is_refused() =>
        Should.Throw<ArgumentException>(() => CombatSimulator.Simulate(
            1, StatFixtures.HeroCurve().At(60), 60, Array.Empty<ActorStats>(), 10));

    /// <summary>
    /// 🔒 `05`'s headnote — <em>"a full 60-second fight must simulate in &lt; 5 ms"</em>, asserted over
    /// the <b>median</b> of N worst-case fights: 1800 ticks, five enemies, three pets, nobody dying.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Asserted at a documented multiple of the budget, and the raw median is printed.</b> The
    /// design number is 5 ms on a target device; this runs on shared CI hardware under a debug build
    /// with a coverage collector attached, where a 3× spread between runs is ordinary. A test pinned
    /// at 5 ms would flake and be deleted, which buys nothing; one at 10× catches the regression that
    /// matters — an accidental per-tick aggregation of every actor, which is roughly 100× — and never
    /// flakes. The printed median is what a human reads to see the real headroom.
    /// </para>
    /// <para>
    /// 🔒 <b>No benchmark project.</b> Nothing authorises one and <c>build/ci/test-suites.json</c>
    /// would need a new entry; this is an ordinary <c>Core.Tests</c> case.
    /// </para>
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

        return CombatSimulator.Simulate(BattleTestBench.Plan(
            actors,
            services => BattleSeams.Strict with { Attack = new RecordingAttackPipeline(services, 1.0) },
            rules: new CombatRules(maxTicks, OnKillTriggersFire: true, IsPvp: false),
            battleSeed: seed));
    }

    private static SimulationResult PublicFight(ulong seed) =>
        CombatSimulator.Simulate(
            seed,
            StatFixtures.HeroCurve().At(60),
            60,
            new[] { BattleTestBench.Stats(maxHp: 300, atk: 8), BattleTestBench.Stats(maxHp: 200, atk: 6) },
            10);
}
