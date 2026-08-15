using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// Covers three boss built-ins: phase-3 STUN/FREEZE immunity, the Core damage-amplification flag,
/// and the boss duration guardrails.
/// </summary>
public sealed class BossBuiltInsTests
{
    private const string Phase2EnterInstance = "BOSS_RIMEHOLD#P2#BOSS_RIMEHOLD_P2_CORE";

    // ════════════════════════════════════════════════════ 1 · phase-3 immunity

    [Fact]
    public void Entering_phase_3_grants_STUN_and_FREEZE_immunity_through_the_status_engine()
    {
        var run = Fight((Tick: 60, ActorId: "BOSS_RIMEHOLD", Fraction: 0.20));

        run.Statuses.Immunities.Count.ShouldBe(2, "one per status, and nothing else");

        run.Statuses.Immunities.Select(i => i.StatusId).Order(StringComparer.Ordinal).ShouldBe(
            new[] { "FREEZE", "STUN" });

        foreach (var immunity in run.Statuses.Immunities)
        {
            immunity.Target.ShouldBe("BOSS_RIMEHOLD", "17 §1: the BOSS is immune, not the hero");
            immunity.Scope.ShouldBe(
                DurationScope.PHASE,
                "⚠️ ASSUMPTION RECORDED: no document states which of 18 §6's six scopes phase-3 " +
                "immunity uses. PHASE is the only one that means 'while in this phase', and phase 3 " +
                "is never exited (05 §3.1), so it is equivalent to BATTLE in practice");
        }
    }

    /// <summary>Negative control: only phase 3 gets the immunity grant, not phase 2.</summary>
    [Fact]
    public void Entering_phase_2_grants_no_immunity()
    {
        var run = Fight((Tick: 60, ActorId: "BOSS_RIMEHOLD", Fraction: 0.50));

        BossTestBench.PhaseChanges(run.Result.Log).Count.ShouldBe(
            2, "the floor: phase 2 WAS entered, so the emptiness below is a rule and not a no-op");

        run.Statuses.Immunities.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("SYS_PHASE3_IMMUNE_STUN", "STUN")]
    [InlineData("SYS_PHASE3_IMMUNE_FREEZE", "FREEZE")]
    public void The_phase_3_immunity_built_ins_are_IMMUNE_STATUS_on_an_ON_PHASE_ENTER_3_trigger(
        string effectId, string statusId)
    {
        var effect = BossBuiltIns.All.Single(e => string.Equals(e.Id, effectId, StringComparison.Ordinal));

        effect.Op.ShouldBe(EffectOp.IMMUNE_STATUS);
        effect.StatusId.ShouldBe(statusId);
        effect.Target.ShouldBe(EffectTarget.SELF);
        effect.Trigger!.Kind.ShouldBe(TriggerKind.ON_PHASE_ENTER);
        effect.Trigger.Phase.ShouldBe(3);
        effect.Duration!.Scope.ShouldBe(DurationScope.PHASE);
    }

    // ════════════════════════════════════════════════════ 2 · Core damage-amplification flag

    /// <summary>The Core is a damage-taken multiplier flag on the boss, not a separate actor.</summary>
    [Fact]
    public void The_damage_amplification_state_flag_is_CombatFlowStates_existing_pair()
    {
        var flow = new CombatFlowState();

        flow.DamageTakenMultiplier().ShouldBe(1.0, "the control: no amplification is ×1");

        flow.AddDamageTakenMultiplier(1.6, "BOSS_RIMEHOLD_P2_CORE");

        flow.DamageTakenMultiplier().ShouldBe(1.6, "17 §6's Core window");

        flow.AddDamageTakenMultiplier(0.8, "PK_STALWART");
        flow.DamageTakenMultiplier().ShouldBe(
            1.28,
            "05 §4 step 6 takes the PRODUCT of every active one — 1.6 x 0.8 — which is why the seam " +
            "is named Add rather than Set");
    }

    /// <remarks>
    /// No boss-engine code was added for this — it's the existing op, product formula, and flow
    /// state meeting at a phase block for free.
    /// </remarks>
    [Fact]
    public void A_phase_block_can_arm_the_Core_state_flag_on_the_boss()
    {
        var run = Fight((Tick: 60, ActorId: "BOSS_RIMEHOLD", Fraction: 0.50));

        var boss = run.Driver.Actors.Single(a => a.IsBoss);

        boss.Flow.DamageTakenMultiplier().ShouldBe(
            1.6, "17 §6: hits deal ×1.6 while the Core is exposed, with no targeting change");
    }

    // ════════════════════════════════════════════════════ 3 · duration guardrails

    /// <remarks>
    /// Deliberately unenforced by the engine: a short fight is a tuning signal for the balance
    /// harness, not an engine-level error — throwing here would turn a balance finding into a
    /// player-facing crash.
    /// </remarks>
    [Fact]
    public void The_duration_guardrails_are_the_numbers_17_1_and_05_9_state()
    {
        BossDurationGuardrails.ParMinSeconds.ShouldBe(35.0);
        BossDurationGuardrails.ParMaxSeconds.ShouldBe(60.0);
        BossDurationGuardrails.HardFloorSeconds.ShouldBe(12.0);
        BossDurationGuardrails.HardCeilingSeconds.ShouldBe(70.0);

        BossDurationGuardrails.HardCeilingSeconds.ShouldBe(
            BossBuiltIns.EnrageStartDelaySeconds,
            "and not by coincidence: 17 §1 says the enrage 'guarantees termination without a draw', " +
            "so the ceiling is the moment the guarantee starts working");
    }

    // ════════════════════════════════════════════════════ fixtures

    private static ActorPlan BossPlan() =>
        BossTestBench.Boss(
            "BOSS_RIMEHOLD",
            maxHp: 1000.0,
            BossTestBench.InPhase("BOSS_RIMEHOLD", 2, BossTestBench.RimeholdCore()),
            BossTestBench.BuiltIn("BOSS_RIMEHOLD", BossBuiltIns.Enrage),
            BossTestBench.BuiltIn("BOSS_RIMEHOLD", BossBuiltIns.Phase3StunImmunity),
            BossTestBench.BuiltIn("BOSS_RIMEHOLD", BossBuiltIns.Phase3FreezeImmunity));

    private static BossEncounter Encounter() => new()
    {
        BossId = "BOSS_RIMEHOLD",
        Plan = BossPlan(),
        FirstClear = false,
        Phase2HpFraction = 0.66,
        Phase3HpFraction = 0.33,
        PhaseOfInstance = new Dictionary<EffectInstanceId, int>
        {
            [EffectInstanceId.Of(Phase2EnterInstance)] = 2,
        },
        LeadSecondsOfInstance = new Dictionary<EffectInstanceId, double>(),

        // No mechanic authors a wind-up here, so nothing is announced.
        AnnouncingOfPhase = new Dictionary<int, IReadOnlyList<EffectInstanceId>>(),
    };

    private static BossRun Fight(params (int Tick, string ActorId, double Fraction)[] script) =>
        BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), BossPlan() },
            new List<BossEncounter> { Encounter() },
            new List<EffectInstanceId> { EffectInstanceId.Of(Phase2EnterInstance) },
            script);
}
