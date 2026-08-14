using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05` §3.1 slot 4 — <c>attackCooldown = 1.0 / ASPD</c>, decremented by <c>TICK</c> every tick.
/// </summary>
public sealed class AttackCadenceTests
{
    /// <summary>
    /// 🔴 A 1.0-ASPD actor attacks on ticks 0, 20, 40, … — <b>exactly</b> once a second, for the
    /// whole 90 s fight.
    /// </summary>
    /// <remarks>
    /// This is the rounding case. `05` §1.1 rounds at every accumulation point, and slot 4b is one:
    /// twenty raw subtractions of 0.05 from 1.0 leave <c>1.3878e-16</c> rather than 0, so
    /// <c>attackCooldown &lt;= 0</c> reads false and the actor waits a twenty-first tick. Over 1800
    /// ticks that is 85 swings instead of 90 — a 5% DPS error the whole balance model would absorb
    /// as content being weak.
    /// </remarks>
    [Fact]
    public void A_1_0_ASPD_actor_swings_on_every_twentieth_tick_for_the_whole_fight()
    {
        var swings = HeroSwingTicks(aspd: 1.0);

        swings.Count.ShouldBe(90);
        swings.ShouldBe(Enumerable.Range(0, 90).Select(i => i * 20).ToArray());
    }

    /// <summary>
    /// 🔴 <b>The case that actually discriminates the rounding, and it is not the one above.</b> A
    /// 2.0-ASPD actor has a 0.5 s cooldown and must swing every tenth tick.
    /// </summary>
    /// <remarks>
    /// 🔴 Unrounded, twenty subtractions of <c>0.05</c> from <c>1.0</c> land on <c>-3.19e-16</c> —
    /// <em>below</em> zero — so the 1.0-ASPD case fires on tick 20 either way and proves nothing about
    /// `05` §1.1's rounding. Ten subtractions from <c>0.5</c> land on <c>+6.94e-17</c>, <em>above</em>
    /// zero, and the actor waits an eleventh tick: 164 swings across a 90 s fight instead of 180, a 9%
    /// DPS error. The sign of a floating-point residue is not something a test should guess at.
    /// </remarks>
    [Fact]
    public void A_2_0_ASPD_actor_swings_on_every_tenth_tick_for_the_whole_fight()
    {
        var swings = HeroSwingTicks(aspd: 2.0);

        swings.Count.ShouldBe(180);
        swings.ShouldBe(Enumerable.Range(0, 180).Select(i => i * 10).ToArray());
    }

    /// <summary>
    /// A cooldown that is not a whole number of ticks. <c>1.0 / 1.6 = 0.625</c> is 12.5 ticks, so the
    /// swing lands on the 13th — the first tick at or past it, never the 12th.
    /// </summary>
    [Fact]
    public void A_fractional_cooldown_lands_on_the_first_tick_at_or_past_it()
    {
        var swings = HeroSwingTicks(aspd: 1.6);

        swings.Take(5).ShouldBe(new[] { 0, 13, 26, 39, 52 });
    }

    /// <summary>
    /// 🔒 <em>"ASPD read at fire time"</em> — an ASPD change landing in slot 1 of tick 20 shortens the
    /// cooldown set by <b>that tick's</b> swing, not the one after it.
    /// </summary>
    /// <remarks>
    /// Driven through <see cref="IStatusTimeline"/>, which is the seam that will really do this:
    /// `05` §5's <c>HASTE</c> is "+X% ASPD" and <c>FREEZE</c> is "−50% ASPD", and both land in slot 1
    /// as M2-10's cadence advances their timers. Slot 1 runs before slot 4 in the same tick, so a
    /// loop that read ASPD from the start-of-tick snapshot would set a 1.0 s cooldown here and the
    /// next swing would be tick 40.
    /// </remarks>
    [Fact]
    public void ASPD_is_read_at_fire_time()
    {
        RecordingAttackPipeline? pipeline = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10_000, aspd: 1.0)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 10_000, aspd: 0.001)),
            },
            services =>
            {
                pipeline = new RecordingAttackPipeline(services, damage: 0.0001);

                return BattleSeams.Strict with
                {
                    Attack = pipeline,
                    Timeline = new HasteAtTick(20, "HERO", aspd: 2.0),
                };
            }));

        pipeline.ShouldNotBeNull();
        pipeline.Swings.Where(s => s.Attacker == "HERO").Select(s => s.Tick).Take(5)
            .ShouldBe(new[] { 0, 20, 30, 40, 50 });
    }

    /// <summary>
    /// 🔒 `05` §4 — <em>"<c>AttackMultiplier</c>, base 1.0 on every basic attack"</em>, and it is what
    /// slot 4 hands <c>IAttackPipeline.ResolveAttack</c> along with the basic-attack source id.
    /// </summary>
    [Fact]
    public void Slot_4_hands_the_pipeline_a_base_multiplier_of_1_and_the_basic_attack_source_id()
    {
        RecordingAttackPipeline? pipeline = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 1000)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 30)),
            },
            services =>
            {
                pipeline = new RecordingAttackPipeline(services);

                return BattleSeams.Strict with { Attack = pipeline };
            }));

        pipeline.ShouldNotBeNull();
        pipeline.Swings.ShouldAllBe(s => s.Multiplier == 1.0);
        pipeline.Swings.ShouldAllBe(s => s.SourceEffectId == BattleSimulation.BasicAttackSourceId);
        pipeline.Swings.Count.ShouldBeGreaterThan(0);

        // Not an authored effect id, and it cannot collide with one: `18` §8 ids are identifiers.
        BattleSimulation.BasicAttackSourceId.ShouldStartWith("(");
    }

    /// <summary>
    /// 🔒 `05` §3.1 — <em>"summons enter … with a full attack cooldown (1.0 / ASPD — they never attack
    /// on their spawn tick) and become targetable at the next targeting evaluation"</em>, at the end
    /// of the enemy index list and on a log id that is never reused.
    /// </summary>
    [Fact]
    public void A_summon_enters_at_the_end_of_the_list_with_a_full_cooldown()
    {
        BattleServices? services = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10_000)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 10_000)),
            },
            s =>
            {
                services = s;

                return BattleSeams.Strict with { Attack = new RecordingAttackPipeline(s, 1.0) };
            },
            rules: new CombatRules(MaxTicks: 3, OnKillTriggersFire: true)));

        services.ShouldNotBeNull();

        var before = services.Actors.Count;
        var summon = services.AdmitSummon(BattleTestBench.Enemy(99, BattleTestBench.Stats(aspd: 2.0)) with
        {
            Id = "SHARD",
            Index = -1,
            LogId = 1,
        });

        services.Actors.Count.ShouldBe(before + 1);
        services.Actors[^1].ShouldBeSameAs(summon);

        // End of the enemy index list, and a log id above every one already taken.
        summon.Index.ShouldBeGreaterThan(services.Actors[before - 1].Index);
        summon.LogId.ShouldBeGreaterThan(services.Actors[before - 1].LogId);

        // 🔒 A FULL cooldown — 1.0 / 2.0 — not the zero every opener gets at pre-tick 0a.
        summon.AttackCooldown.ShouldBe(0.5);
        summon.IsSummon.ShouldBeTrue();
    }

    /// <summary>
    /// 🔒 A swing that never resolved does not consume the cooldown — the same cost as finding no
    /// target at all.
    /// </summary>
    /// <remarks>
    /// `05` §3.1 step 6 puts an actor out of play <em>"at that moment"</em>, so an <c>ON_ATTACK</c>
    /// trigger that finishes the target leaves the attacker with nothing to hit. Slot 4a already
    /// costs nothing when the target list is empty at selection time; dying two lines later is the
    /// same situation and must cost the same, or an actor whose own trigger secured the kill loses a
    /// whole attack cycle for it.
    /// </remarks>
    [Fact]
    public void A_swing_whose_ON_ATTACK_trigger_kills_the_target_does_not_spend_the_cooldown()
    {
        RecordingAttackPipeline? pipeline = null;
        BattleServices? services = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                // The hero's ON_ATTACK deals 10 true damage before the swing resolves, which is
                // exactly ENEMY_0's health. ENEMY_1 survives, so the fight continues.
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10_000, aspd: 1.0), 1, new HeldEffect(
                    new EffectDefinition
                    {
                        Id = "A_OPENING_STRIKE",
                        Op = EffectOp.DAMAGE_TRUE,
                        Value = 10.0,
                        Target = EffectTarget.CURRENT_TARGET,
                        Trigger = new EffectTrigger { Kind = TriggerKind.ON_ATTACK },
                    })),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 10, aspd: 0.001)),
                BattleTestBench.Enemy(1, BattleTestBench.Stats(maxHp: 10_000, aspd: 0.001)),
            },
            s =>
            {
                services = s;
                pipeline = new RecordingAttackPipeline(s, damage: 1.0);

                return BattleSeams.Strict with { Attack = pipeline };
            },
            rules: new CombatRules(MaxTicks: 3, OnKillTriggersFire: true)));

        pipeline.ShouldNotBeNull();
        services.ShouldNotBeNull();

        // 🔒 Tick 0: the trigger killed ENEMY_0 before the swing resolved, so ResolveAttack was never
        // reached and the cooldown was never spent — and the hero therefore swings again on tick 1,
        // at ENEMY_1. A loop that charged for the unresolved swing would make it wait until tick 20.
        pipeline.Swings.Where(s => s.Attacker == "HERO")
            .Select(s => (s.Tick, s.Defender))
            .ShouldBe(new[] { (1, "ENEMY_1") });

        // ENEMY_0 really did die on tick 0, so the case above is the one described and not a fight
        // in which the trigger simply missed.
        services.Actors.Single(a => a.Id == "ENEMY_0").IsAlive.ShouldBeFalse();
    }

    private static List<int> HeroSwingTicks(double aspd)
    {
        RecordingAttackPipeline? pipeline = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 1_000_000, aspd: aspd)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 1_000_000, aspd: 0.001)),
            },
            services =>
            {
                pipeline = new RecordingAttackPipeline(services, damage: 0.0001);

                return BattleSeams.Strict with { Attack = pipeline };
            }));

        return pipeline!.Swings.Where(s => s.Attacker == "HERO").Select(s => s.Tick).ToList();
    }
}

/// <summary>
/// A slot-1 timeline that rewrites one actor's ASPD on a given tick — `05` §5's <c>HASTE</c>, in the
/// slot M2-10 will apply it from.
/// </summary>
internal sealed class HasteAtTick : IStatusTimeline
{
    private readonly int _tick;
    private readonly string _actorId;
    private readonly double _aspd;

    internal HasteAtTick(int tick, string actorId, double aspd)
    {
        _tick = tick;
        _actorId = actorId;
        _aspd = aspd;
    }

    /// <inheritdoc />
    public void AdvanceTimers(BattleActor actor, int tick)
    {
        if (tick == _tick && string.Equals(actor.Id, _actorId, StringComparison.Ordinal))
        {
            actor.SetStats(actor.Aggregated with { Final = actor.Stats.With(StatId.ASPD, _aspd) });
        }
    }

    /// <inheritdoc />
    public void ExpireDue(BattleActor actor, int tick)
    {
    }

    /// <inheritdoc />
    public bool CanAct(BattleActor actor) => true;

    /// <inheritdoc />
    public int StacksOn(BattleActor actor, string statusId) => 0;

    /// <inheritdoc />
    public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor) => [];
}
