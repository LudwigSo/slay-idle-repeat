using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Tests.Rules.Combat.Status;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 M2-R1 — a FIRED `18` §2.1 stat op reaches `18` §8's aggregation, through the real
/// <see cref="BattleSimulation"/> tick loop and not a unit fixture that calls
/// <c>StatAggregation.Aggregate</c> directly.
/// </summary>
/// <remarks>
/// Every case here runs a real fight — <c>CombatSimulator.Simulate</c> over a real
/// <see cref="BattleSimulation"/> — and reads the FIRING actor's own aggregated
/// <see cref="BattleActor.Stats"/>, never the trigger registry's bookkeeping alone. That is the
/// distinction the M2-R1 handover draws: <c>SysEnrageAnchoringTests</c> already proved the enrage
/// fires on schedule and is never re-anchored by a phase transition; nothing before this file proved
/// firing it changed anything.
/// </remarks>
public sealed class TriggeredStatOpTests
{
    private const string Thornmaw = BossTestBench.Thornmaw;

    // ════════════════════════════════════════════════════ 1 · SYS_ENRAGE's magnitude

    /// <summary>
    /// 🔒 R1 / the M2-R1 acceptance criterion, stated exactly: three seconds of <c>SYS_ENRAGE</c> is
    /// <c>100 × 1.08³ = 125.9712</c>, through the real loop.
    /// </summary>
    [Fact]
    public void Three_seconds_of_SYS_ENRAGE_multiplies_ATK_to_125_9712()
    {
        // Three activations: the built-in fires at 70s (tick 1400), 71s (1420) and 72s (1440) —
        // TriggerSchedule's arithmetic, pinned separately by SysEnrageAnchoringTests. Run past the
        // third so the tick loop's top-of-tick RefreshStats has picked it up.
        var run = EnrageOnlyFight(maxTicks: 1450);
        var boss = run.Driver.Actors.Single(a => a.Id == Thornmaw);

        boss.Stats[StatId.ATK].ShouldBe(
            125.9712,
            "🔒 R1 — STAT_MULT's value IS the multiplier: 100 x 1.08^3 = 125.9712, not " +
            "100 x 2.08^3 = 899.8912 (05 §1.1's erratum) and not a single x1.08 = 108.0 (the bug)");
    }

    /// <summary>
    /// 🔒 M2-R1 acceptance item 5 — a fired stat op applies EXACTLY once per activation. Sampled at
    /// three ticks strictly between the first and second firing: an implementation that re-applied
    /// SYS_ENRAGE every tick (rather than reading <c>EffectStackSet.CombinedValue</c> once per
    /// activation) would show ATK climbing tick over tick; the real one holds it flat at
    /// <c>100 x 1.08 = 108.0</c> until the second activation at tick 1420.
    /// </summary>
    [Fact]
    public void A_fired_stat_op_does_not_reapply_on_every_tick_between_activations()
    {
        var run = EnrageOnlyFight(maxTicks: 1419);
        var boss = run.Driver.Actors.Single(a => a.Id == Thornmaw);

        // The single sample this run's tick loop leaves behind is as of tick 1418 — the top of the
        // LAST tick this run executed (0..1418), which is strictly between the first firing (1400)
        // and the second (1420). One fired stack, and only one, must be showing.
        boss.Stats[StatId.ATK].ShouldBe(108.0, "one activation of SYS_ENRAGE: 100 x 1.08^1");

        // Re-run to a second and a third point in that same open interval and confirm the reading is
        // IDENTICAL every time — not "some other wrong number", the specific symptom a per-tick
        // re-application bug produces (steering S2).
        EnrageOnlyFight(maxTicks: 1410).Driver.Actors.Single(a => a.Id == Thornmaw)
            .Stats[StatId.ATK].ShouldBe(108.0, "tick 1409 is also strictly between the two firings");

        EnrageOnlyFight(maxTicks: 1402).Driver.Actors.Single(a => a.Id == Thornmaw)
            .Stats[StatId.ATK].ShouldBe(108.0, "one tick after the firing itself");
    }

    private static BossRun EnrageOnlyFight(int maxTicks)
    {
        var plan = BossTestBench.Boss(Thornmaw, maxHp: 1_000_000.0, BossTestBench.BuiltIn(
            Thornmaw, BossBuiltIns.Enrage)) with
        {
            BaseStats = BattleTestBench.Stats(maxHp: 1_000_000.0, atk: 100.0, aspd: 0.001),
        };

        var encounter = new BossEncounter
        {
            BossId = Thornmaw,
            Plan = plan,
            FirstClear = false,
            Phase2HpFraction = 0.66,
            Phase3HpFraction = 0.33,
            PhaseOfInstance = new Dictionary<EffectInstanceId, int>(),
            LeadSecondsOfInstance = new Dictionary<EffectInstanceId, double>(),
            AnnouncingOfPhase = new Dictionary<int, IReadOnlyList<EffectInstanceId>>(),
        };

        return BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), plan },
            new List<BossEncounter> { encounter },
            new List<EffectInstanceId> { BossBuiltIns.BuiltInInstance(Thornmaw, BossBuiltIns.EnrageId) },
            new List<(int, string, double)>(),
            maxTicks: maxTicks);
    }

    // ════════════════════════════════════════════════════ 2 · a PHASE-scoped fired stat op

    /// <summary>
    /// 🔒 M2-R1 acceptance item 4 — a `18` §6 <c>PHASE</c> scope still correctly ends a boss's fired
    /// stat op at phase exit, exactly as it already did for a boss <c>AURA</c> authored as a status
    /// (M2-12's wiring). Regression-proofed explicitly because M2-R1 touches the SAME aggregation
    /// pass that scope depends on: a fold that forgot to consult <see cref="TriggeredStatInstance"/>'s
    /// duration would leave a fired <c>STAT_ADD_PCT</c> live for the rest of the battle.
    /// </summary>
    /// <remarks>
    /// Three independent fights, cut at three points, rather than one fight sampled mid-flight — the
    /// same technique <c>SysEnrageAnchoringTests</c> uses via <c>BossDriver.At</c>, applied here to the
    /// aggregated STAT rather than to the trigger registry's bookkeeping.
    /// </remarks>
    [Fact]
    public void A_PHASE_scoped_fired_stat_op_is_live_only_inside_its_phase()
    {
        const string overheatId = "TEST_PHASE2_ATK_BUFF";

        BossRun Fight(int maxTicks) => PhaseBuffFight(overheatId, maxTicks);

        // Before phase 2 (the script's first step is at tick 10) — never fired.
        Fight(5).Driver.Actors.Single(a => a.Id == Thornmaw)
            .Stats[StatId.ATK].ShouldBe(100.0, "phase 2 has not been entered yet");

        // Inside phase 2 (entered at tick 10, phase 3 not yet at tick 30) — the buff is live.
        Fight(15).Driver.Actors.Single(a => a.Id == Thornmaw)
            .Stats[StatId.ATK].ShouldBe(150.0, "100 x 1.5 — the phase-2 STAT_ADD_PCT ATK +50% fired");

        // After phase 3 (entered at tick 30) — 18 §6 PHASE ends the phase-2 block at the exit.
        Fight(40).Driver.Actors.Single(a => a.Id == Thornmaw)
            .Stats[StatId.ATK].ShouldBe(
                100.0,
                "the fired stat op's PHASE scope ended at the phase-2 exit, exactly as it does for a " +
                "boss AURA authored as a status");
    }

    private static BossRun PhaseBuffFight(string overheatId, int maxTicks)
    {
        var phase2Instance = BossBuiltIns.PhaseInstance(Thornmaw, 2, overheatId);

        var plan = BossTestBench.Boss(
            Thornmaw,
            maxHp: 1000.0,
            BossTestBench.InPhase(Thornmaw, 2, BossTestBench.PhaseStatBuff(overheatId, 2, StatId.ATK, 0.5)))
            with
            {
                BaseStats = BattleTestBench.Stats(maxHp: 1000.0, atk: 100.0, aspd: 0.001),
            };

        var encounter = new BossEncounter
        {
            BossId = Thornmaw,
            Plan = plan,
            FirstClear = false,
            Phase2HpFraction = 0.66,
            Phase3HpFraction = 0.33,
            PhaseOfInstance = new Dictionary<EffectInstanceId, int> { [phase2Instance] = 2 },
            LeadSecondsOfInstance = new Dictionary<EffectInstanceId, double>(),
            AnnouncingOfPhase = new Dictionary<int, IReadOnlyList<EffectInstanceId>>(),
        };

        return BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), plan },
            new List<BossEncounter> { encounter },
            new List<EffectInstanceId> { phase2Instance },
            new List<(int, string, double)>
            {
                (10, Thornmaw, 0.60), // into phase 2 — the buff fires
                (30, Thornmaw, 0.20), // into phase 3 — 18 §6 ends the phase-2 block
            },
            maxTicks: maxTicks);
    }

    // ════════════════════════════════════════════════════ 3 · a boss AURA granting RAGE

    /// <summary>
    /// 🔒 M2-R1 acceptance item 2 — a boss <c>AURA</c> granting <c>RAGE</c> measurably changes ATK
    /// through the real loop. Unlike the two sections above, <c>RAGE</c> is authored as
    /// <c>APPLY_STATUS</c> (`17` §2's Thornmaw phase-3 RAGE, `18` §7.8's idiom) — a path M2-10 wired
    /// and M2-R1 does not touch — so this is a confirmation the two paths agree, not a red-then-green
    /// proof of THIS fix.
    /// </summary>
    [Fact]
    public void A_boss_AURA_granting_RAGE_measurably_raises_ATK()
    {
        StatusTimeline? timeline = null;
        BattleSimulation? simulation = null;

        var plan = BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 100_000.0, atk: 0.0)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 100_000.0, atk: 100.0)),
            },
            services =>
            {
                var pipeline = new RecordingAttackPipeline(services, damage: 0.0);
                timeline = new StatusTimeline(services, pipeline, StatusFixtures.Catalogue());

                return BattleSeams.Strict with
                {
                    Attack = pipeline,
                    Statuses = timeline,
                    Timeline = timeline,
                };
            },
            rules: new CombatRules(5, OnKillTriggersFire: true));

        simulation = new BattleSimulation(plan);
        var enemy = simulation.Actors.Single(a => a.Id == "ENEMY_0");

        enemy.Stats[StatId.ATK].ShouldBe(100.0, "the floor: baseline ATK before any RAGE");

        // `17` §2 / `18` §7.8's idiom: APPLY_STATUS RAGE, 30% — read straight off StatusFixtures'
        // catalogue row (TARGET_STAT_PCT on ATK).
        timeline!.Apply(
            enemy, enemy, "RAGE", potency: 0.3,
            duration: new EffectDuration { Scope = DurationScope.BATTLE },
            stacking: null,
            sourceEffectId: "TEST_BOSS_AURA_RAGE");

        simulation.Run();

        enemy.Stats[StatId.ATK].ShouldBe(130.0, "05 §5's RAGE is +X% ATK, and 18 §8 step 5 picks it up");
    }
}
