using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Bosses;
using SlayIdleRepeat.Core.Rules.Effects;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `17` §1's second universal built-in — <em>"Bosses are immune to <c>STUN</c> and <c>FREEZE</c>
/// in phase 3"</em> — plus `17` §11's two remaining checklist rows: the <c>Core</c> damage-amp state
/// flag, and `17` §1's duration guardrails.
/// </summary>
public sealed class BossBuiltInsTests
{
    private const string Phase2EnterInstance = "BOSS_RIMEHOLD#P2#BOSS_RIMEHOLD_P2_CORE";

    // ════════════════════════════════════════════════════ 1 · phase-3 immunity

    /// <summary>
    /// 🔒 `17` §1 — entering phase 3 grants <c>STUN</c> and <c>FREEZE</c> immunity, through
    /// <see cref="IStatusEngine.GrantImmunity"/> and as <b>data</b>: an <c>IMMUNE_STATUS</c> on an
    /// <c>ON_PHASE_ENTER {phase: 3}</c> trigger, attached to every boss by the builder.
    /// </summary>
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

    /// <summary>
    /// 🔒 The negative control: phase <b>2</b> grants neither. `17` §1 gives the immunity to phase 3
    /// alone, and phases 1–2 keep `05` §5's ordinary 3 s stun-immunity window — which is M2-10's and
    /// is <b>not</b> implemented here.
    /// </summary>
    [Fact]
    public void Entering_phase_2_grants_no_immunity()
    {
        var run = Fight((Tick: 60, ActorId: "BOSS_RIMEHOLD", Fraction: 0.50));

        BossTestBench.PhaseChanges(run.Result.Log).Count.ShouldBe(
            2, "the floor: phase 2 WAS entered, so the emptiness below is a rule and not a no-op");

        run.Statuses.Immunities.ShouldBeEmpty();
    }

    /// <summary>
    /// The built-ins as data: `18` §2.3's <c>IMMUNE_STATUS</c>, on <c>SELF</c>, fired by phase 3's
    /// entry, one per status.
    /// </summary>
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

    // ════════════════════════════════════════════════════ 2 · `17` §11's Core state flag

    /// <summary>
    /// 🔒 `17` §6's ruling — <em>"the Core is not a separate actor. It is a state flag on the boss
    /// that multiplies incoming damage by 1.6 … no targeting logic changes"</em>. `17` §11 asks for
    /// exactly that flag, and the pair that satisfies it already exists.
    /// </summary>
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

    /// <summary>
    /// 🔒 And it is <b>reachable from a boss phase block</b>, which is the half `17` §11's checklist
    /// row actually asks about: a <c>DAMAGE_TAKEN_MULT</c> authored on Rimehold's phase-2 entry lands
    /// on the boss's own flow state when the phase is entered.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Nothing is implemented for this.</b> `18` §2.4's op, `05` §4 step 6's product and the
    /// flow state all shipped in M2-03/M2-08, and M2-09 applies the multiplier inside the damage
    /// pipeline. This case is the pin that the three meet at a boss phase block, and the boss engine
    /// adds no code to the path.
    /// </remarks>
    [Fact]
    public void A_phase_block_can_arm_the_Core_state_flag_on_the_boss()
    {
        var run = Fight((Tick: 60, ActorId: "BOSS_RIMEHOLD", Fraction: 0.50));

        var boss = run.Driver.Actors.Single(a => a.IsBoss);

        boss.Flow.DamageTakenMultiplier().ShouldBe(
            1.6, "17 §6: hits deal ×1.6 while the Core is exposed, with no targeting change");
    }

    // ════════════════════════════════════════════════════ 3 · `17` §1's duration guardrails

    /// <summary>
    /// 🔒 `17` §1 / `05` §9 — <em>"35–60 s at par power … never below 12 s, never above 70 s"</em>,
    /// as named numbers the balance harness reads rather than as engine throws.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>Nothing in the engine enforces these and nothing should.</b> A fight that runs 8 s is a
    /// tuning failure the harness (M2-16a) reports across a distribution; an engine that refused it
    /// would turn a balance finding into a crash inside a player's run — and would stop the harness
    /// measuring the very thing it exists to measure.
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

        // No mechanic here authors a wind-up, so nothing is announced in any phase.
        AnnouncingOfPhase = new Dictionary<int, IReadOnlyList<EffectInstanceId>>(),
    };

    private static BossRun Fight(params (int Tick, string ActorId, double Fraction)[] script) =>
        BossTestBench.Run(
            new List<ActorPlan> { BossTestBench.Hero(), BossPlan() },
            new List<BossEncounter> { Encounter() },
            new List<EffectInstanceId> { EffectInstanceId.Of(Phase2EnterInstance) },
            script);
}
