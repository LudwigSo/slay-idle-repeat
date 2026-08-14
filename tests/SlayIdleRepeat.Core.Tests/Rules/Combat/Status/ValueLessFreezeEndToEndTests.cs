using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Status;

/// <summary>
/// 🔒 M2-R3 (3b), end to end: a value-less <c>APPLY_STATUS FREEZE</c> — the exact shape
/// <c>BOSS_RIMEHOLD_P2_SHATTERBACK_FREEZE</c> authors — driven through the REAL DSL resolver
/// (<c>EffectOpResolver</c> → <c>StatusOps.Apply</c> → the real <c>StatusTimeline</c>) rather than
/// <see cref="StatusTimelineTests"/>'s <c>ScriptedApplications</c> decorator, which calls
/// <c>StatusTimeline.Apply</c> directly and so never reaches <see cref="StatusOps"/> or
/// <see cref="Values.ValueScaleEvaluator"/> at all.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What "end to end" means here, precisely.</b> The fight goes: a <c>PERIODIC</c> trigger fires
/// → <c>EffectOpResolver.Resolve</c> dispatches <c>APPLY_STATUS</c> → <c>StatusOps.Apply</c> calls the
/// new <c>Potency</c> helper, which asks <c>IStatusEngine.HasFixedPotency("FREEZE")</c> (answered by
/// the real <see cref="StatusCatalogue"/>, read from <see cref="StatusFixtures.Snapshot"/> — the same
/// shape the shipped <c>content/statuses.json</c> takes) → the real <see cref="StatusTimeline.Apply"/>
/// reads <c>definition.FixedPotency</c> (−0.5) → `18` §8's aggregation folds it into the actor's
/// <c>ASPD</c>. Every one of those hops is real; nothing is scripted or recorded.
/// </para>
/// <para>
/// ⚠️ The target is <c>SELF</c>, not <c>ATTACKER</c> — the shipped effect's own token (it debuffs
/// whoever hit the boss). This test is deliberately about 3b in isolation, with the simplest trigger
/// and target that lets the holder debuff its own ASPD; <c>AllAuthoredBossesRunToCompletionTests</c>
/// is what proves the shipped RIMEHOLD script, with its own <c>ON_HIT_TAKEN</c>/<c>ATTACKER</c> shape,
/// runs end to end.
/// </para>
/// </remarks>
public sealed class ValueLessFreezeEndToEndTests
{
    /// <summary>
    /// 🔒 The headline numeric claim: FREEZE, applied with no authored <c>value</c>, still halves the
    /// target's aggregated ASPD — `05` §5's −50 %, read off the status's own row, not invented and not
    /// refused.
    /// </summary>
    [Fact]
    public void A_value_less_APPLY_STATUS_FREEZE_halves_the_targets_ASPD()
    {
        var freeze = new EffectDefinition
        {
            Id = "TEST_SHATTERBACK_FREEZE",
            Op = EffectOp.APPLY_STATUS,
            StatusId = "FREEZE",
            Value = null, // 🔒 the exact shape BOSS_RIMEHOLD_P2_SHATTERBACK_FREEZE authors
            Target = EffectTarget.SELF,
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 1.0 },
            Duration = new EffectDuration { Scope = DurationScope.BATTLE, Seconds = 999.0 },
        };

        var simulation = Run(freeze, targetAspd: 2.0, ticks: 40);

        var enemy = simulation.Actors.Single(a => a.Id == "ENEMY_0");

        enemy.Stats[StatId.ASPD].ShouldBe(1.0, "05 §5's FREEZE is -50% ASPD, and the base here is 2.0");
    }

    /// <summary>
    /// 🔒 Negative control — the same fight, but the <c>value</c> is a plain 0.3 and the status is
    /// RAGE (no <c>FixedPotency</c>), to show this bench measures a real aggregation and is not simply
    /// unable to detect a stat change.
    /// </summary>
    [Fact]
    public void The_bench_detects_an_ordinary_authored_status_potency_too()
    {
        var rage = new EffectDefinition
        {
            Id = "TEST_RAGE",
            Op = EffectOp.APPLY_STATUS,
            StatusId = "RAGE",
            Value = 0.3,
            Target = EffectTarget.SELF,
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 1.0 },
            Duration = new EffectDuration { Scope = DurationScope.BATTLE, Seconds = 999.0 },
        };

        var simulation = Run(rage, targetAspd: 2.0, ticks: 40);

        var enemy = simulation.Actors.Single(a => a.Id == "ENEMY_0");

        enemy.Stats[StatId.ATK].ShouldBe(13.0, "10 base ATK x (1 + 0.3) RAGE");
    }

    /// <summary>
    /// Runs a real fight — the real <c>EffectOpResolver</c>/<c>StatusOps</c>/<c>StatusTimeline</c>,
    /// none of them faked — in which <c>ENEMY_0</c> holds <paramref name="effect"/> and fires it on
    /// its own authored trigger, and returns the live simulation so the caller can read aggregated
    /// stats after the run.
    /// </summary>
    private static BattleSimulation Run(EffectDefinition effect, double targetAspd, int ticks)
    {
        StatusTimeline? timeline = null;

        var plan = BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 100_000.0, atk: 0.0)),
                BattleTestBench.Enemy(
                    0,
                    BattleTestBench.Stats(maxHp: 100_000.0, atk: 10.0, aspd: targetAspd),
                    effects: new[] { new HeldEffect(effect) }),
            },
            services =>
            {
                var attack = new RecordingStatusPipeline(services);
                timeline = new StatusTimeline(services, attack, StatusFixtures.Catalogue());

                return BattleSeams.Strict with
                {
                    Attack = attack,
                    Statuses = timeline,
                    Timeline = timeline,
                };
            },
            rules: new CombatRules(ticks, OnKillTriggersFire: true));

        var simulation = new BattleSimulation(plan);
        simulation.Run();

        return simulation;
    }
}
