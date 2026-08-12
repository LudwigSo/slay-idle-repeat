using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `05` §3.1's phase check and pre-tick 0c, `17` §1's three phases — the entry sequence, the
/// R8 anchors it moves, and the two things it must never do.
/// </summary>
/// <remarks>
/// 🔴 <b>Every anchoring case here asserts <c>AnchorTick</c> and/or <c>NextFiringTick</c>, never
/// merely "the effect fired".</b> <c>TriggerRegistry.Activate</c> leaves a live instance untouched,
/// so a controller that omitted the <c>Deactivate</c> half of a transition would still produce a
/// fight in which every mechanic fires — at the wrong anchor, in a log nothing distinguishes from
/// correct. That is the probe M2-08's phase-anchoring case could not fail, and it is the reason
/// <see cref="BossDriver"/> samples the registry rather than the log.
/// </remarks>
public sealed class BossPhaseControllerTests
{
    private const string Phase1Instance = "BOSS_THORNMAW#P1#Z_P1_BASK";
    private const string Phase2RootInstance = "BOSS_THORNMAW#P2#BOSS_THORNMAW_P2_ROOT";
    private const string Phase2EnterInstance = "BOSS_THORNMAW#P2#Z_P2_SNAP";
    private const string Phase3EnterInstance = "BOSS_THORNMAW#P3#A_P3_BLOOM";

    // 🔒 The ids are chosen so ascending effect-id order and phase order DISAGREE: `A_P3_BLOOM`
    // sorts before `Z_P2_SNAP`. A controller that swept both entries as one occurrence, or that
    // entered the phases in the wrong order, would produce the alphabetical order instead.
    private const string Phase1Effect = "Z_P1_BASK";
    private const string Phase2Effect = "Z_P2_SNAP";
    private const string Phase3Effect = "A_P3_BLOOM";

    // ════════════════════════════════════════════════════ 1 · pre-tick 0c

    /// <summary>
    /// 🔒 `05` §3.1 — pre-tick 0c runs <b>after</b> 0b's <c>ON_BATTLE_START</c> sweep and
    /// <b>before</b> 0d's <c>BattleStart</c>, so phase 1's entry effects land on an arena that is
    /// already in its opening state and the replayer's banner covers all of it.
    /// </summary>
    [Fact]
    public void Pre_tick_0c_enters_phase_1_after_the_ON_BATTLE_START_sweep_and_before_BattleStart()
    {
        var statuses = new RecordingStatuses();

        var result = Fight(statuses: statuses, opener: true).Result;

        statuses.Applied.ShouldBe(
            new[] { "Z_BOSS_OPENER", Phase1Effect },
            Case.Sensitive,
            "0b sweeps ON_BATTLE_START, then 0c enters phase 1");

        var events = result.Log.ToList();
        var phaseChange = events.FindIndex(e => e.Type == CombatEventType.PhaseChange);
        var battleStart = events.FindIndex(e => e.Type == CombatEventType.BattleStart);

        phaseChange.ShouldBeGreaterThanOrEqualTo(0, "the floor: phase 1's entry IS logged");
        battleStart.ShouldBeGreaterThanOrEqualTo(
            0,
            "and the second floor: 0d's banner IS logged. Without it a log that never reached 0d " +
            "would make the ordering claim below a comparison against -1");
        phaseChange.ShouldBeLessThan(battleStart, "0c precedes 0d");
    }

    /// <summary>
    /// 🔴 <b>THE DE-ANCHORING.</b> Pre-tick 0a registers <b>every</b> plan effect at activation tick
    /// 0, phase-2 and phase-3 blocks included — so 0c has to drop those clocks, or Thornmaw's 8 s
    /// Root ticks from t = 8 s while the boss is still basking.
    /// </summary>
    /// <remarks>
    /// 🔴 <b>The mutant this discriminates against:</b> remove the <c>Deactivate</c> from
    /// <c>EnterInitialPhase</c> and the instance is live at tick 0 with <c>AnchorTick == 0</c> and
    /// <c>NextFiringTick == 160</c> (8 s × 20). Asserting only that the Root fired would pass either
    /// way.
    /// </remarks>
    [Fact]
    public void EnterInitialPhase_de_anchors_every_phase_2_and_phase_3_instance()
    {
        var driver = Fight().Driver;

        var root = driver.At(0, Phase2RootInstance);

        root.IsRegistered.ShouldBeTrue(
            "the floor: the phase-2 block IS on ActorPlan.Effects, so the battle's effect table can " +
            "name it and CombatLog.AppendTelegraph can index it");
        root.IsActive.ShouldBeFalse("18 §6's PHASE scope has not begun — the boss is in phase 1");
        root.AnchorTick.ShouldBeNull(
            "🔴 R8's clock is DROPPED, not merely idle: an anchor of 0 here would make the Root fire " +
            "at t = 8 s, in phase 1, in a fight nobody authored");
        root.NextFiringTick.ShouldBeNull();

        driver.At(0, Phase3EnterInstance).IsActive.ShouldBeFalse("phase 3 is de-anchored too");

        driver.At(0, Phase1Instance).IsActive.ShouldBeTrue(
            "the negative control: phase 1's own block is LIVE, so the sweep is not simply " +
            "deactivating everything");
    }

    /// <summary>
    /// 🔴 The other half of the same claim: entering phase 2 <b>re-anchors</b> its block at the entry
    /// tick — R8's <em>"the clock starts when the owning effect becomes active"</em>, which for a
    /// phase block is `17` §1.1's <em>"fires every N seconds from phase entry"</em>.
    /// </summary>
    [Fact]
    public void Entering_phase_2_anchors_its_PERIODIC_at_the_entry_tick()
    {
        var driver = Fight(script: new[] { (Tick: 100, ActorId: BossTestBench.Thornmaw, Fraction: 0.50) }).Driver;

        var root = driver.At(101, Phase2RootInstance);

        root.IsActive.ShouldBeTrue();
        root.AnchorTick.ShouldBe(100, "the entry tick IS the anchor");
        root.NextFiringTick.ShouldBe(
            260, "an absent startDelay is one interval: 100 + 8 s x 20 ticks");
    }

    // ════════════════════════════════════════════════════ 2 · the entry sequence

    /// <summary>
    /// 🔴 <b>The two-threshold burst.</b> `05` §3.1: <em>"a burst from 70% to 20% therefore fires
    /// phase 2's entry, then phase 3's"</em> — two entries inside <b>one</b> HP decrease, in that
    /// order.
    /// </summary>
    [Fact]
    public void A_burst_from_70_to_20_percent_fires_phase_2s_entry_then_phase_3s()
    {
        var statuses = new RecordingStatuses();

        var result = Fight(
            statuses: statuses,
            script: new[]
            {
                (Tick: 40, ActorId: BossTestBench.Thornmaw, Fraction: 0.70),
                (Tick: 100, ActorId: BossTestBench.Thornmaw, Fraction: 0.20),
            }).Result;

        var changes = BossTestBench.PhaseChanges(result.Log);

        changes.Count.ShouldBe(3, "phase 1 at the pre-tick, then 2 and 3 on the burst");
        changes.Select(e => e.Value).ShouldBe(new[] { 1.0, 2.0, 3.0 });
        changes.Skip(1).Select(e => e.Tick).ShouldBe(
            new[] { 100, 100 }, "both entries are inside the ONE decrease that caused them");

        statuses.Applied.ShouldBe(
            new[] { Phase1Effect, Phase2Effect, Phase3Effect },
            Case.Sensitive,
            "🔒 the ORDER, not merely the presence: `A_P3_BLOOM` sorts BEFORE `Z_P2_SNAP`, so a " +
            "controller that swept both entries as one occurrence in ascending effect-id order " +
            "would report them the other way round");
    }

    /// <summary>
    /// 🔴 <b>Phases never revert.</b> `05` §3.1: healing back above a threshold does not re-enter,
    /// and the exited phases' `18` §6 <c>PHASE</c>-scoped effects stay dead.
    /// </summary>
    [Fact]
    public void A_boss_healed_back_above_a_threshold_does_not_re_enter_a_phase()
    {
        var run = Fight(script: new[]
        {
            (Tick: 40, ActorId: BossTestBench.Thornmaw, Fraction: 0.20),
            (Tick: 80, ActorId: BossTestBench.Thornmaw, Fraction: 0.90),
            (Tick: 120, ActorId: BossTestBench.Thornmaw, Fraction: 0.85),
        });

        var changes = BossTestBench.PhaseChanges(run.Result.Log);

        changes.Count.ShouldBe(
            3, "phase 1, then 2 and 3 on the burst at tick 40 — and nothing after the heal");
        changes.Select(e => e.Tick).ShouldBe(new[] { 0, 40, 40 });

        run.Controller.CurrentPhaseOf(BossTestBench.Thornmaw).ShouldBe(
            BossPhaseRules.FinalPhase, "the phase is the controller's state, not a reading of HP");

        run.Driver.At(140, Phase1Instance).IsActive.ShouldBeFalse(
            "phase 1's PHASE-scoped block ended at its exit and a heal does not re-grant it");
        run.Driver.At(140, Phase2RootInstance).IsActive.ShouldBeFalse(
            "nor phase 2's — the boss is in phase 3");
    }

    /// <summary>
    /// 🔒 `18` §6's <c>PHASE</c> scope — <em>"ends when the boss exits the phase in which the effect
    /// was applied"</em>. The exit is the entry of the next phase, and nothing else ends it.
    /// </summary>
    [Fact]
    public void A_PHASE_scoped_block_ends_at_the_exit_of_its_own_phase()
    {
        var driver = Fight(script: new[]
        {
            (Tick: 100, ActorId: BossTestBench.Thornmaw, Fraction: 0.50),
        }).Driver;

        driver.At(100, Phase1Instance).IsActive.ShouldBeTrue(
            "the control: phase 1's block is live right up to the entry");
        driver.At(101, Phase1Instance).IsActive.ShouldBeFalse("and dead the moment phase 2 begins");
        driver.At(101, Phase2EnterInstance).IsActive.ShouldBeTrue("while phase 2's is live");
        driver.At(101, Phase3EnterInstance).IsActive.ShouldBeFalse("and phase 3's is still de-anchored");
    }

    /// <summary>
    /// 🔒 <c>CombatEvent</c>'s slot contract for <c>PhaseChange</c>, stated exhaustively there
    /// because a slot left to the emitter's judgement is a slot `11` §6 reads as tampering:
    /// <em>"the boss · the boss · the phase entered (1, 2 or 3) · 0"</em>.
    /// </summary>
    [Fact]
    public void PhaseChange_names_the_boss_in_both_slots_and_carries_the_phase_as_its_value()
    {
        var result = Fight(script: new[]
        {
            (Tick: 100, ActorId: BossTestBench.Thornmaw, Fraction: 0.50),
        }).Result;

        var entered = BossTestBench.PhaseChanges(result.Log);

        entered.Count.ShouldBe(2, "the floor under the per-event assertions below");

        foreach (var change in entered)
        {
            change.SourceId.ShouldBe(CombatActor.Enemy(0));
            change.TargetId.ShouldBe(CombatActor.Enemy(0), "both slots are the boss, not None");
            change.DataId.ShouldBe(CombatLog.NoDataId, "PhaseChange names no content");
        }

        entered[1].Value.ShouldBe(2.0);
        entered[1].Tick.ShouldBe(100);
    }

    // ════════════════════════════════════════════════════ 3 · what the check ignores

    /// <summary>
    /// 🔒 The check is handed <b>every</b> actor — <em>"'is this a boss' is the controller's
    /// question"</em> — and answers nothing for one that is not a boss.
    /// </summary>
    /// <remarks>
    /// 🔴 The two floors are what make the emptiness a rule rather than an accident. A script step
    /// whose actor id matched nothing would leave every HP untouched and no check ever asked, and
    /// <em>"one PhaseChange"</em> would then be true for a reason that has nothing to do with `05`
    /// §3.1's question (steering S1/S3).
    /// </remarks>
    [Fact]
    public void An_HP_decrease_on_a_non_boss_actor_enters_no_phase()
    {
        var run = Fight(
            withMinion: true,
            script: new[]
            {
                (Tick: 60, ActorId: "ENEMY_1", Fraction: 0.10),
                (Tick: 80, ActorId: "HERO", Fraction: 0.10),
            });

        run.Driver.Actors.Single(a => string.Equals(a.Id, "ENEMY_1", StringComparison.Ordinal))
           .HpFraction.ShouldBe(0.10, "the floor: the minion really did lose HP");
        run.Driver.Actors.Single(a => string.Equals(a.Id, "HERO", StringComparison.Ordinal))
           .HpFraction.ShouldBe(0.10, "and so did the hero");
        run.Driver.Actors.Single(a => a.IsBoss).HpFraction.ShouldBe(
            1.0, "while the boss itself never lost any");

        var changes = BossTestBench.PhaseChanges(run.Result.Log);

        changes.Count.ShouldBe(1, "only pre-tick 0c's phase 1 — the boss itself never lost HP");
        changes[0].Value.ShouldBe(1.0);
    }

    /// <summary>
    /// 🔒 A boss on the roster that this controller has no <see cref="BossEncounter"/> for is a
    /// <b>wiring gap</b>, refused at pre-tick 0c — the same shape <c>NoBossPhases</c> uses, and for
    /// its reason: a boss whose mechanics are silently absent reads to the balance harness as a boss
    /// that is weak.
    /// </summary>
    [Fact]
    public void A_boss_with_no_encounter_is_refused_at_pre_tick_0c()
    {
        var thrown = Should.Throw<EffectContextException>(() => CombatSimulator.Simulate(
            BattleTestBench.Plan(
                new[] { BossTestBench.Hero(), BossTestBench.Boss("BOSS_UNSCRIPTED") },
                services => BattleSeams.Strict with
                {
                    Attack = new RecordingAttackPipeline(services, damage: 0.0),
                    Phases = new BossPhaseController(services, Encounter()),
                },
                rules: BossTestBench.Rules(maxTicks: 5))));

        // 🔒 Steering S2 — WHICH rule fired, not merely that something did. EffectContextException is
        //    thrown by a dozen independent rules in this layer, so the type alone discriminates
        //    nothing; the boss's id and the missing encounter are what name this one.
        thrown.Message.ShouldStartWith(EffectContextException.Marker, Case.Sensitive);
        thrown.Message.ShouldContain("BOSS_UNSCRIPTED", Case.Sensitive, "which boss");
        thrown.Message.ShouldContain("encounter", Case.Insensitive, "and what it is missing");
        thrown.Message.ShouldContain(
            BossTestBench.Thornmaw,
            Case.Sensitive,
            "and what it DOES hold — a refusal that did not say would send M2-13 looking for a " +
            "controller with no bosses at all rather than one with the wrong boss");
    }

    // ════════════════════════════════════════════════════ 4 · the first-clear extension

    /// <summary>
    /// 🔴 `17` §1 — <em>"the first time a player fights a boss, phase 1 lasts 20% longer"</em>. On a
    /// repeat clear the boundary is 0.66; on a first clear it is 0.5920, and 0.66 enters nothing.
    /// </summary>
    /// <remarks>
    /// 🔴 The expectation is the <b>whole</b> <c>PhaseChange</c> sequence, not a count of the 2s.
    /// <c>Count(e =&gt; e.Value == 2.0).ShouldBe(0)</c> stood here for the first-clear row and passed
    /// on an empty log — a controller that entered no phase at all, logged nothing, or was never
    /// reached satisfied it exactly (steering S1). Pinning the sequence floors phase 1's own entry,
    /// which no first-clear rule touches.
    /// </remarks>
    [Theory]
    [InlineData(false, new[] { 1.0, 2.0 })]
    [InlineData(true, new[] { 1.0 })]
    public void At_exactly_66_percent_a_repeat_clear_enters_phase_2_and_a_first_clear_does_not(
        bool firstClear, double[] expectedPhases)
    {
        var run = Fight(
            firstClear: firstClear,
            script: new[] { (Tick: 100, ActorId: BossTestBench.Thornmaw, Fraction: 0.66) });

        run.Driver.Actors.Single(a => a.IsBoss).HpFraction.ShouldBe(
            0.66, "the floor: the boss really was put on 17 §1's boundary");

        BossTestBench.PhaseChanges(run.Result.Log).Select(e => e.Value).ShouldBe(
            expectedPhases,
            "17 §1 triggers phase 2 AT 66%, so <= — and the first clear moves that boundary to " +
            "0.5920, at which 66% is still phase 1 while phase 1's own pre-tick entry is unchanged");
    }

    /// <summary>
    /// 🔴 And the widened boundary is reached at 0.5920 exactly, while `17` §1's phase-3 boundary is
    /// untouched: <b>only phase 1 is extended</b>, so phase 2 absorbs the difference.
    /// </summary>
    [Fact]
    public void A_first_clear_enters_phase_2_at_0_5920_and_phase_3_still_at_0_33()
    {
        var result = Fight(
            firstClear: true,
            script: new[]
            {
                (Tick: 60, ActorId: BossTestBench.Thornmaw, Fraction: 0.5920),
                (Tick: 120, ActorId: BossTestBench.Thornmaw, Fraction: 0.33),
            }).Result;

        var changes = BossTestBench.PhaseChanges(result.Log);

        changes.Select(e => (e.Tick, e.Value)).ShouldBe(
            new[] { (0, 1.0), (60, 2.0), (120, 3.0) },
            "the extension widens phase 1's band by 20%; 17 §1's 0.33 is not extended");
    }

    // ════════════════════════════════════════════════════ fixtures

    /// <summary>
    /// `17` §2's Thornmaw as a plan: a phase-1 aura, a phase-2 <c>ON_PHASE_ENTER</c> plus the 8 s
    /// Root, a phase-3 <c>ON_PHASE_ENTER</c>, and <c>SYS_ENRAGE</c>.
    /// </summary>
    private static ActorPlan BossPlan(bool opener) =>
        BossTestBench.Boss(
            BossTestBench.Thornmaw,
            maxHp: 1000.0,
            Holdings(opener).ToArray());

    private static IEnumerable<HeldEffect> Holdings(bool opener)
    {
        yield return BossTestBench.InPhase(
            BossTestBench.Thornmaw, 1, BossTestBench.OnPhaseEnter(Phase1Effect, 1));
        yield return BossTestBench.InPhase(
            BossTestBench.Thornmaw, 2, BossTestBench.OnPhaseEnter(Phase2Effect, 2));
        yield return BossTestBench.InPhase(BossTestBench.Thornmaw, 2, BossTestBench.Root());
        yield return BossTestBench.InPhase(
            BossTestBench.Thornmaw, 3, BossTestBench.OnPhaseEnter(Phase3Effect, 3));
        yield return BossTestBench.BuiltIn(BossTestBench.Thornmaw, BossBuiltIns.Enrage);

        if (opener)
        {
            yield return new HeldEffect(
                new EffectDefinition
                {
                    Id = "Z_BOSS_OPENER",
                    Op = EffectOp.APPLY_STATUS,
                    StatusId = "RAGE",
                    Value = 0.1,
                    Target = EffectTarget.SELF,
                    Trigger = new EffectTrigger { Kind = TriggerKind.ON_BATTLE_START },
                },
                EffectInstanceId.Of($"{BossTestBench.Thornmaw}#OPENER"));
        }
    }

    /// <summary>The encounter the controller is given — hand-built, so these cases do not depend on
    /// <see cref="BossEncounterBuilder"/>.</summary>
    private static BossEncounter Encounter(bool firstClear = false, bool opener = false) =>
        new()
        {
            BossId = BossTestBench.Thornmaw,
            Plan = BossPlan(opener),
            FirstClear = firstClear,

            // 🔒 Literals, not BossPhaseRules.Phase2HpFraction: a controller test that took its
            // expectation from the same method the controller reads would agree with a wrong
            // threshold. BossPhaseRulesTests pins the method against `17` §1 separately.
            Phase2HpFraction = firstClear ? 0.5920 : 0.66,
            Phase3HpFraction = 0.33,
            PhaseOfInstance = new Dictionary<EffectInstanceId, int>
            {
                [EffectInstanceId.Of(Phase1Instance)] = 1,
                [EffectInstanceId.Of(Phase2EnterInstance)] = 2,
                [EffectInstanceId.Of(Phase2RootInstance)] = 2,
                [EffectInstanceId.Of(Phase3EnterInstance)] = 3,
            },
            LeadSecondsOfInstance = new Dictionary<EffectInstanceId, double>(),

            // No mechanic here authors a wind-up, so nothing is announced in any phase.
            AnnouncingOfPhase = new Dictionary<int, IReadOnlyList<EffectInstanceId>>(),
        };

    /// <summary>One scripted fight against `17` §2's Thornmaw.</summary>
    private static BossRun Fight(
        RecordingStatuses? statuses = null,
        bool firstClear = false,
        bool opener = false,
        bool withMinion = false,
        params (int Tick, string ActorId, double Fraction)[] script)
    {
        var roster = new List<ActorPlan> { BossTestBench.Hero(), BossPlan(opener) };
        if (withMinion)
        {
            roster.Add(BossTestBench.Minion(1));
        }

        return BossTestBench.Run(
            roster,
            new List<BossEncounter> { Encounter(firstClear, opener) },
            Watched,
            script,
            statuses);
    }

    private static IReadOnlyList<EffectInstanceId> Watched { get; } = new List<EffectInstanceId>
    {
        EffectInstanceId.Of(Phase1Instance),
        EffectInstanceId.Of(Phase2EnterInstance),
        EffectInstanceId.Of(Phase2RootInstance),
        EffectInstanceId.Of(Phase3EnterInstance),
        EffectInstanceId.Of($"{BossTestBench.Thornmaw}#{BossBuiltIns.EnrageId}"),
    };
}
