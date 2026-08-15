using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// <c>SYS_ENRAGE</c> is universal, unmodified, and not a phase: it anchors at battle start and no
/// phase transition may ever touch it.
/// </summary>
/// <remarks>
/// A <c>PERIODIC</c> is anchored when its effect becomes active, which for a plan effect is
/// pre-tick 0a. That is what makes <c>startDelay: 70.0</c> mean 70 seconds of battle. A phase entry
/// that deactivated and reactivated it would re-anchor the clock and the boss would enrage 70 s
/// after reaching 66% HP — a fight that runs to the 90 s timeout with nothing in the log to explain
/// it.
/// </remarks>
public sealed class SysEnrageAnchoringTests
{
    private const string EnrageInstance = "BOSS_THORNMAW#SYS_ENRAGE";
    private const string Phase2Instance = "BOSS_THORNMAW#P2#BOSS_THORNMAW_P2_ROOT";

    // ════════════════════════════════════════════════════ 1 · the discriminating probe

    /// <summary>
    /// The probe that must discriminate. In one fight: the phase-2 mechanic's anchor does move to
    /// the entry tick, and <c>SYS_ENRAGE</c>'s does not — so the case proves the controller
    /// distinguishes the two rather than proving it touched nothing.
    /// </summary>
    [Fact]
    public void A_phase_entry_re_anchors_the_phase_block_and_leaves_SYS_ENRAGE_alone()
    {
        var driver = Fight((Tick: 100, ActorId: BossTestBench.Thornmaw, Fraction: 0.50)).Driver;

        var enrageBefore = driver.At(100, EnrageInstance);
        var enrageAfter = driver.At(101, EnrageInstance);

        enrageBefore.AnchorTick.ShouldBe(0, "pre-tick 0a is SYS_ENRAGE's R8 anchor");
        enrageBefore.NextFiringTick.ShouldBe(1400, "startDelay 70 s x 20 ticks, from BATTLE start");

        enrageAfter.AnchorTick.ShouldBe(
            enrageBefore.AnchorTick, "🔒 a phase entry must not re-anchor a BATTLE-scoped built-in");
        enrageAfter.NextFiringTick.ShouldBe(
            1400,
            "re-anchoring at tick 100 would put the first stack at 1500 — the boss enraging 70 s " +
            "after reaching 66% HP instead of 70 s into the fight");
        enrageAfter.IsActive.ShouldBeTrue("and it was never deactivated either");

        // The discriminator: the same transition did move the phase block's clock.
        driver.At(101, Phase2Instance).AnchorTick.ShouldBe(
            100, "the control — a controller that touched nothing would fail here");
    }

    /// <summary>And it survives two entries inside one tick — the 70% to 20% burst, the case that would re-anchor twice.</summary>
    /// <remarks>
    /// The controls are what make this a probe rather than a wish: asserting only that the enrage
    /// did not move would pass on a controller that entered no phase at all. So the same fight
    /// asserts that two further entries were logged, and that the phase-2 block's own clock was
    /// touched by them.
    /// </remarks>
    [Fact]
    public void SYS_ENRAGE_survives_a_burst_that_crosses_two_thresholds_in_one_tick()
    {
        var run = Fight((Tick: 80, ActorId: BossTestBench.Thornmaw, Fraction: 0.20));

        run.Driver.At(81, EnrageInstance).AnchorTick.ShouldBe(0);
        run.Driver.At(81, EnrageInstance).NextFiringTick.ShouldBe(1400);
        run.Driver.At(81, EnrageInstance).IsActive.ShouldBeTrue();

        // The discriminators, in the same fight.
        var changes = BossTestBench.PhaseChanges(run.Result.Log);

        changes.Select(e => (e.Tick, e.Value)).ShouldBe(
            new[] { (0, 1.0), (80, 2.0), (80, 3.0) },
            "the control: the burst really did cross both thresholds, in order, at that tick");

        run.Driver.At(81, Phase2Instance).IsActive.ShouldBeFalse(
            "and the SAME transitions did reach the phase map — phase 2 was entered and then " +
            "exited, so its PHASE-scoped block is dead while SYS_ENRAGE, which is not in the map, " +
            "is untouched");
    }

    /// <summary>It is not in the phase map at all — the structural reason a transition cannot reach it, rather than a rule the controller has to remember.</summary>
    [Fact]
    public void SYS_ENRAGEs_instance_id_carries_no_phase_and_is_absent_from_the_phase_map()
    {
        var encounter = Encounter();

        encounter.PhaseOfInstance.Count.ShouldBe(1, "the floor: the map is not empty");
        encounter.PhaseOfInstance.Keys.Select(k => k.Value).ShouldNotContain(EnrageInstance);

        BossBuiltIns.BuiltInInstance(BossTestBench.Thornmaw, BossBuiltIns.EnrageId)
                    .Value.ShouldBe(EnrageInstance);

        BossBuiltIns.BuiltInInstance(BossTestBench.Thornmaw, BossBuiltIns.EnrageId)
                    .Value!.Contains("#P", StringComparison.Ordinal)
                    .ShouldBeFalse("a built-in's id carries no phase, deliberately");
    }

    // ════════════════════════════════════════════════════ 2 · the built-in as data

    /// <summary><c>PERIODIC {interval: 1.0, startDelay: 70.0}</c> to <c>STAT_MULT ATK x1.08</c>, <c>BATTLE</c> scope, uncapped multiplicative stacking.</summary>
    [Fact]
    public void The_built_in_enrage_is_the_effect_05_3_1_and_17_1_author()
    {
        var enrage = BossBuiltIns.Enrage;

        enrage.Id.ShouldBe("SYS_ENRAGE");
        enrage.Op.ShouldBe(EffectOp.STAT_MULT, "17 §1's '+8% ATK per second, compounding'");
        enrage.Stat!.Value.Stat.ShouldBe(StatId.ATK);
        enrage.Value.ShouldBe(
            1.08,
            "🔒 R1 — STAT_MULT's value IS the multiplier, so three seconds is 1.08^3 = 1.259712 and " +
            "not 2.08^3 = 8.998912 (05 §1.1's literal Pi(1 + v) reading is the erratum)");
        enrage.Target.ShouldBe(EffectTarget.SELF);

        enrage.Trigger!.Kind.ShouldBe(TriggerKind.PERIODIC);
        enrage.Trigger.Interval.ShouldBe(1.0);
        enrage.Trigger.StartDelay.ShouldBe(70.0);

        enrage.Duration!.Scope.ShouldBe(
            DurationScope.BATTLE, "🔒 not PHASE — a PHASE scope would end it at the first exit");
        enrage.Stacking!.Mode.ShouldBe(StackingMode.MULTIPLICATIVE);
        enrage.Stacking.MaxStacks.ShouldBeNull("05 §3.1: 'uncapped'");
    }

    /// <summary>The first firing is at 70 s of battle time, and it is <see cref="TriggerSchedule"/>'s arithmetic, not a second copy of it.</summary>
    [Fact]
    public void The_enrage_first_fires_on_the_tick_70_seconds_of_battle_time_lands_on()
    {
        TriggerSchedule.FirstFiringTick(BossBuiltIns.Enrage.Trigger!, anchorTick: 0)
                       .ShouldBe(BossTestBench.At(70.0));

        BossTestBench.At(70.0).ShouldBe(1400, "the floor under the assertion above");
    }

    /// <summary>
    /// No Sporequeen exemption: the enrage is implemented once, applied to all bosses, so it is
    /// attached by the builder rather than authored.
    /// </summary>
    [Fact]
    public void The_enrage_is_a_built_in_rather_than_something_a_boss_script_can_author()
    {
        BossBuiltIns.All.Count.ShouldBe(3, "the floor under the membership assertion below");
        BossBuiltIns.All.Select(e => e.Id).ShouldContain(BossBuiltIns.EnrageId);

        // The script shape: mechanics are effect ids, and there is no per-boss enrage switch
        // anywhere on it.
        //
        // AddsPowerFraction is on this shape too, and it is not a counter-example: it's a number
        // the design hands to content — a power band for a boss's adds — which is the opposite of
        // an engine behaviour a boss can opt out of.
        var shape = typeof(BossScript).GetProperties().Select(p => p.Name).ToArray();

        // The named claims come first, and the exhaustive one is the backstop behind them. Written
        // the other way round these three were unreachable: the exhaustive ShouldBe fails on any
        // added property, so a hypothetical `EnrageExempt` tripped the generic "the shape moved"
        // assertion and never reached the assertion that says why that particular shape is forbidden.
        foreach (var builtIn in BossBuiltIns.All)
        {
            shape.ShouldNotContain(
                name => name.Contains(builtIn.Id, StringComparison.OrdinalIgnoreCase),
                $"a script field named after '{builtIn.Id}' would be the per-boss opt-out the " +
                "design exists to prevent — the enrage is implemented once, for all eight");
        }

        shape.ShouldNotContain(
            name => name.Contains("Enrage", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Immun", StringComparison.OrdinalIgnoreCase),
            "the same claim in the spelling a future author is likelier to reach for");

        shape.ShouldBe(
            new[] { "Id", "Coefficients", "AddsPowerFraction", "Phases" }, ignoreOrder: true);
    }

    // ════════════════════════════════════════════════════ fixtures

    private static ActorPlan BossPlan() =>
        BossTestBench.Boss(
            BossTestBench.Thornmaw,
            maxHp: 1000.0,
            BossTestBench.InPhase(BossTestBench.Thornmaw, 2, BossTestBench.Root()),
            BossTestBench.BuiltIn(BossTestBench.Thornmaw, BossBuiltIns.Enrage));

    private static BossEncounter Encounter() => new()
    {
        BossId = BossTestBench.Thornmaw,
        Plan = BossPlan(),
        FirstClear = false,
        Phase2HpFraction = 0.66,
        Phase3HpFraction = 0.33,
        PhaseOfInstance = new Dictionary<EffectInstanceId, int>
        {
            [EffectInstanceId.Of(Phase2Instance)] = 2,
        },
        LeadSecondsOfInstance = new Dictionary<EffectInstanceId, double>(),

        // No mechanic authors a wind-up here, so nothing is announced.
        AnnouncingOfPhase = new Dictionary<int, IReadOnlyList<EffectInstanceId>>(),
    };

    private static BossRun Fight(params (int Tick, string ActorId, double Fraction)[] script) =>
        BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), BossPlan() },
            new List<BossEncounter> { Encounter() },
            new List<EffectInstanceId>
            {
                EffectInstanceId.Of(EnrageInstance),
                EffectInstanceId.Of(Phase2Instance),
            },
            script);
}
