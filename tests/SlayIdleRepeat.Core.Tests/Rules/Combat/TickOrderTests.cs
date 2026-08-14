using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// 🔒 `05` §3.1 — the battle-start pre-tick and the strict eight-step tick order.
/// </summary>
public sealed class TickOrderTests
{
    /// <summary>
    /// 🔒 Pre-tick 0d — <em>"emit BattleStart"</em>, last, and at tick 0. Every 0b ward grant and
    /// opening buff is already in the log before it.
    /// </summary>
    [Fact]
    public void The_pre_tick_emits_BattleStart_at_tick_0_naming_no_actor()
    {
        var result = Fight();

        var first = result.Log[0];
        first.Type.ShouldBe(CombatEventType.BattleStart);
        first.Tick.ShouldBe(0);
        first.SourceId.ShouldBe(CombatActor.None);
        first.TargetId.ShouldBe(CombatActor.None);

        result.Log[^1].Type.ShouldBe(CombatEventType.BattleEnd);
    }

    /// <summary>
    /// 🔒 Pre-tick 0a — <em>"attackCooldown = 0 for all battle-opening actors (the first basic attack
    /// lands on tick 0)"</em>.
    /// </summary>
    [Fact]
    public void Every_opener_swings_on_tick_0()
    {
        RecordingAttackPipeline? pipeline = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 1000)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 1000)),
                BattleTestBench.Enemy(1, BattleTestBench.Stats(maxHp: 1000)),
            },
            services =>
            {
                pipeline = new RecordingAttackPipeline(services, damage: 1.0);

                return BattleSeams.Strict with { Attack = pipeline };
            }));

        pipeline.ShouldNotBeNull();
        pipeline.Swings.Where(s => s.Tick == 0)
            .Select(s => s.Attacker)
            .ShouldBe(new[] { "HERO", "ENEMY_0", "ENEMY_1" });
    }

    /// <summary>
    /// 🔒 Slot 4 — <em>"fixed initiative order (Hero, then enemies by index — pets never
    /// basic-attack)"</em>, and the same order on every tick.
    /// </summary>
    /// <remarks>
    /// 🔴 The pet assertion is on the <i>cooldown</i>, not the swing list: "no pet appears among the
    /// attackers" <b>cannot fail</b>, because a pet that <em>was</em> in the order still never swings —
    /// the enemy-side selection finds no opposing hero for it. Slot 4b decrements
    /// <c>attackCooldown</c> for <b>every</b> actor it walks, so a pet still at <c>0</c> after 1800 ticks
    /// is proof it was never in the order.
    /// </remarks>
    [Fact]
    public void Initiative_is_hero_then_enemies_by_index_and_never_a_pet()
    {
        RecordingAttackPipeline? pipeline = null;
        BattleServices? services = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10_000)),
                BattleTestBench.Pet(0),
                BattleTestBench.Pet(1),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 10_000)),
                BattleTestBench.Enemy(1, BattleTestBench.Stats(maxHp: 10_000)),
                BattleTestBench.Enemy(2, BattleTestBench.Stats(maxHp: 10_000)),
            },
            s =>
            {
                services = s;
                pipeline = new RecordingAttackPipeline(s, damage: 1.0);

                return BattleSeams.Strict with { Attack = pipeline };
            }));

        pipeline.ShouldNotBeNull();
        services.ShouldNotBeNull();

        // Every attacking tick shows the same sequence, and no pet ever appears in it.
        foreach (var tick in pipeline.Swings.Select(s => s.Tick).Distinct())
        {
            pipeline.Swings.Where(s => s.Tick == tick)
                .Select(s => s.Attacker)
                .ShouldBe(new[] { "HERO", "ENEMY_0", "ENEMY_1", "ENEMY_2" });
        }

        pipeline.Swings.ShouldAllBe(s => !s.Attacker.StartsWith("PET", StringComparison.Ordinal));
        pipeline.Swings.ShouldAllBe(s => !s.Defender.StartsWith("PET", StringComparison.Ordinal));
        pipeline.Swings.Count.ShouldBeGreaterThan(0);

        // 🔴 The identity: a pet was never walked by slot 4 at all, so nothing ever decremented its
        // cooldown off pre-tick 0a's zero. See the remarks.
        // The count is asserted first so ShouldAllBe cannot pass over an empty collection.
        var pets = services.Actors.Where(a => a.Kind == EffectActorKind.PET).ToArray();
        pets.Length.ShouldBe(2);
        pets.ShouldAllBe(p => p.AttackCooldown == 0.0);
    }

    /// <summary>
    /// 🔒 Slots 1, 2 and 5 run once per actor per tick, in `05` §3.1's actor order — hero, pets in
    /// slot order, enemies by index.
    /// </summary>
    [Fact]
    public void Slots_1_2_and_5_run_once_per_actor_in_actor_order()
    {
        var timeline = new RecordingTimeline();
        var pets = new RecordingPets();

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 1000)),
                BattleTestBench.Pet(0),
                BattleTestBench.Pet(1),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 30)),
            },
            services => BattleSeams.Strict with
            {
                Attack = new RecordingAttackPipeline(services),
                Timeline = timeline,
                Pets = pets,
            }));

        timeline.Calls.Where(c => c.EndsWith("@0", StringComparison.Ordinal))
            .ShouldBe(new[]
            {
                "1:HERO@0", "1:PET_0@0", "1:PET_1@0", "1:ENEMY_0@0",
                "2:HERO@0", "2:PET_0@0", "2:PET_1@0", "2:ENEMY_0@0",
            });

        pets.Calls.Where(c => c.EndsWith("@0", StringComparison.Ordinal))
            .ShouldBe(new[] { "5:PET_0@0", "5:PET_1@0" });
    }

    /// <summary>
    /// 🔒 Slot 4a — <em>"if attackCooldown &lt;= 0 and actor.alive <b>and not stunned</b>"</em>.
    /// </summary>
    [Fact]
    public void A_stunned_actor_does_not_swing()
    {
        var timeline = new RecordingTimeline();
        timeline.Stunned.Add("ENEMY_0");

        RecordingAttackPipeline? pipeline = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 1000)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 1000)),
            },
            services =>
            {
                pipeline = new RecordingAttackPipeline(services, damage: 1.0);

                return BattleSeams.Strict with { Attack = pipeline, Timeline = timeline };
            }));

        pipeline.ShouldNotBeNull();
        pipeline.Swings.ShouldAllBe(s => s.Attacker == "HERO");
        pipeline.Swings.Count.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// 🔒 Slot 6 — <em>"an actor whose HP reaches 0 stops acting and being targetable at that moment
    /// — only its death <i>resolution</i> waits for this slot"</em>, and the event is emitted there.
    /// </summary>
    [Fact]
    public void A_dead_enemy_stops_being_targetable_immediately_and_is_buried_at_slot_6()
    {
        RecordingAttackPipeline? pipeline = null;

        var result = CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                // The hero one-shots. ENEMY_0 dies inside slot 4 on tick 0; ENEMY_1 must then be the
                // hero's target on the next swing, and ENEMY_0 must never swing back.
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10_000)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 10)),
                BattleTestBench.Enemy(1, BattleTestBench.Stats(maxHp: 10_000)),
            },
            services =>
            {
                pipeline = new RecordingAttackPipeline(services, damage: 10.0);

                return BattleSeams.Strict with { Attack = pipeline };
            }));

        pipeline.ShouldNotBeNull();

        // ENEMY_0 died on tick 0 to the hero's swing, which is BEFORE its own place in initiative.
        pipeline.Swings.Where(s => s.Tick == 0).Select(s => s.Attacker)
            .ShouldBe(new[] { "HERO", "ENEMY_1" });

        // And it was buried in the same tick's slot 6.
        var death = result.Log.First(e => e.Type == CombatEventType.ActorDeath);
        death.Tick.ShouldBe(0);
        death.TargetId.ShouldBe(CombatActor.Enemy(0));

        // The hero's next swing goes to the survivor.
        pipeline.Swings.Where(s => s.Attacker == "HERO").Skip(1).First().Defender.ShouldBe("ENEMY_1");
    }

    /// <summary>
    /// 🔒 Slot 8 — <em>"if hero dead OR all enemies dead: break"</em>. The fight stops on the tick the
    /// last enemy falls, not on the one after.
    /// </summary>
    [Fact]
    public void The_loop_breaks_on_the_tick_the_last_enemy_falls()
    {
        var result = CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10_000)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 10)),
            },
            services => BattleSeams.Strict with { Attack = new RecordingAttackPipeline(services, 10.0) }));

        result.HeroWon.ShouldBeTrue();
        result.DurationTicks.ShouldBe(1);
        result.Log[^1].Tick.ShouldBe(0);
    }

    /// <summary>
    /// 🔒 Pre-tick 0c — <em>"the boss's phase 1 counts as entered"</em>, and the strict controller
    /// refuses a boss rather than running one with its mechanics silently deleted.
    /// </summary>
    [Fact]
    public void Pre_tick_0c_enters_the_bosss_phase_1_and_the_strict_default_refuses_a_boss()
    {
        RecordingPhases? phases = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10_000)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 20), isBoss: true),
            },
            services =>
            {
                phases = new RecordingPhases(services);

                return BattleSeams.Strict with
                {
                    Attack = new RecordingAttackPipeline(services),
                    Phases = phases,
                };
            }));

        phases.ShouldNotBeNull();
        phases.Calls[0].ShouldBe("enter1:ENEMY_0@0");

        var refused = Should.Throw<EffectContextException>(() => CombatSimulator.Simulate(
            BattleTestBench.Plan(new[]
            {
                BattleTestBench.Hero(),
                BattleTestBench.Enemy(0, isBoss: true),
            })));

        refused.Message.ShouldContain("M2-12");
    }

    /// <summary>
    /// 🔒 Pre-tick 0b's order — <em>"hero side first (hero, then pets in slot order), then enemies by
    /// index; within one actor, ascending effect-id order"</em>.
    /// </summary>
    [Fact]
    public void ON_BATTLE_START_fires_hero_side_first_then_enemies_and_by_effect_id_within_an_actor()
    {
        var fired = new List<string>();

        var plan = BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 1000), 1,
                    Opener("B_HERO_SECOND"), Opener("A_HERO_FIRST")),
                BattleTestBench.Pet(0, Opener("C_PET")),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 10), effects: Opener("D_ENEMY")),
            },
            services => BattleSeams.Strict with
            {
                Attack = new RecordingAttackPipeline(services, 10.0),
                Statuses = new CapturingStatusEngine(fired),
            });

        CombatSimulator.Simulate(plan);

        fired.ShouldBe(new[] { "A_HERO_FIRST", "B_HERO_SECOND", "C_PET", "D_ENEMY" });

        // An APPLY_STATUS with an ON_BATTLE_START trigger: the only op whose seam this suite can
        // observe without standing in for M2-09.
        static HeldEffect Opener(string id) => new(new EffectDefinition
        {
            Id = id,
            Op = EffectOp.APPLY_STATUS,
            StatusId = "RAGE",
            Value = 0.1,
            Target = EffectTarget.SELF,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_BATTLE_START },
        });
    }

    private static SimulationResult Fight() =>
        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 1000)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 20)),
            },
            services => BattleSeams.Strict with { Attack = new RecordingAttackPipeline(services) }));
}

/// <summary>A status engine that records the effect ids that reached it, in call order.</summary>
internal sealed class CapturingStatusEngine : IStatusEngine
{
    private readonly List<string> _applied;

    internal CapturingStatusEngine(List<string> applied) => _applied = applied;

    /// <inheritdoc />
    public void Apply(
        IEffectActorView applier, IEffectActorView target, string statusId, double potency,
        EffectDuration? duration, EffectStacking? stacking, string sourceEffectId) =>
        _applied.Add(sourceEffectId);

    /// <inheritdoc />
    public bool HasFixedPotency(string statusId) => false;

    /// <inheritdoc />
    public void Remove(IEffectActorView target, string statusId, string sourceEffectId)
    {
    }

    /// <inheritdoc />
    public void RemoveByTag(IEffectActorView target, StatusTag tag, string sourceEffectId)
    {
    }

    /// <inheritdoc />
    public void Extend(IEffectActorView target, string statusId, double seconds, string sourceEffectId)
    {
    }

    /// <inheritdoc />
    public void GrantImmunity(
        IEffectActorView target, string statusId, EffectDuration? duration, string sourceEffectId)
    {
    }

    /// <inheritdoc />
    public void ScaleOutgoingPower(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId)
    {
    }

    /// <inheritdoc />
    public void ScaleIncomingDuration(
        IEffectActorView target, double fraction, EffectDuration? duration, string sourceEffectId)
    {
    }
}
