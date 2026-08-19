using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Ops;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>The tick loop against the trigger registry's wiring contract: which slot asks the registry what.</summary>
/// <remarks>
/// Internal seam: anchor ticks, re-registration and per-slot check calls are not in the log, so the
/// cases observe them through recording seams; the outcomes the wiring produces are asserted at the
/// public entry points elsewhere.
/// </remarks>
public sealed class TriggerWiringTests
{
    /// <summary>
    /// A phase-scoped <c>PERIODIC</c> anchors once at the phase entry and never re-anchors, however
    /// many further HP decreases arrive: the phase check runs after every boss HP decrease, so a
    /// controller that re-registered on each one would push the anchor forward every swing.
    /// </summary>
    [Fact]
    public void A_phase_scoped_PERIODIC_anchors_once_at_the_phase_entry_and_never_re_anchors()
    {
        var fired = new List<int>();
        RecordingPhases? phases = null;
        BattleServices? services = null;

        var mechanic = new EffectDefinition
        {
            Id = "BOSS_ROOT",
            Op = EffectOp.APPLY_STATUS,
            StatusId = "FREEZE",
            Value = 0.5,
            Target = EffectTarget.SELF,
            Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 1.0 },
        };

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                // 1 hit a tick, 1000 HP boss: it crosses 66% on tick 340 and keeps taking hits.
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 1_000_000, aspd: 20.0)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 1000, aspd: 0.001), isBoss: true),
            },
            s =>
            {
                services = s;
                phases = new RecordingPhases(s, mechanic);

                return BattleSeams.Strict with
                {
                    Attack = new RecordingAttackPipeline(s, damage: 1.0),
                    Phases = phases,
                    Statuses = new TickCapturingStatusEngine(fired, s),
                };
            }));

        phases.ShouldNotBeNull();
        services.ShouldNotBeNull();

        // Entered exactly once, and the check ran on every one of the boss's HP decreases.
        phases.Entries.ShouldBe(1);
        phases.Calls.Count(c => c.StartsWith("check:ENEMY_0@", StringComparison.Ordinal))
            .ShouldBeGreaterThan(100);

        var anchor = phases.AnchoredAt!.Value;

        // startDelay is absent, so the interval runs from the anchor — the first firing is exactly
        // 20 ticks (1.0 s) after the phase entry, and every subsequent one is 20 apart.
        fired.ShouldNotBeEmpty();
        fired[0].ShouldBe(anchor + 20);
        fired.Zip(fired.Skip(1), (a, b) => b - a).ShouldAllBe(gap => gap == 20);

        // And the instance's own anchor never moved off the tick it was registered on.
        services.Triggers[EffectInstanceId.Of("ENEMY_0#phase2")].AnchorTick.ShouldBe(anchor);
    }

    /// <summary>
    /// A <c>BATTLE</c>-scoped periodic anchors at battle start, which is what makes an enrage's
    /// <c>startDelay: 70.0</c> mean 70 seconds of battle.
    /// </summary>
    [Fact]
    public void SYS_ENRAGE_anchors_at_battle_start_so_its_startDelay_is_70_seconds_of_battle()
    {
        var fired = new List<int>();

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 1_000_000, aspd: 0.001)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 1_000_000, aspd: 0.001), effects:
                    new HeldEffect(new EffectDefinition
                    {
                        Id = "SYS_ENRAGE",
                        Op = EffectOp.APPLY_STATUS,
                        StatusId = "RAGE",
                        Value = 0.08,
                        Target = EffectTarget.SELF,
                        Trigger = new EffectTrigger
                        {
                            Kind = TriggerKind.PERIODIC,
                            Interval = 1.0,
                            StartDelay = 70.0,
                        },
                    })),
            },
            services => BattleSeams.Strict with
            {
                Attack = new RecordingAttackPipeline(services, damage: 0.0),
                Statuses = new TickCapturingStatusEngine(fired, services),
            }));

        // Tick 1400 is 70.0 s exactly.
        fired[0].ShouldBe(1400);
        BattleClock.SecondsAt(fired[0]).ShouldBe(70.0);

        // 90 s cap - 70 s delay = 20 firings, one a second.
        fired.Count.ShouldBe(20);
        fired[^1].ShouldBe(1780);
    }

    /// <summary><c>PeriodicDue</c> is the only <c>PERIODIC</c> path — the registry throws on the other.</summary>
    [Fact]
    public void A_PERIODIC_evaluated_through_the_moment_path_is_refused_by_the_registry()
    {
        var registry = new TriggerRegistry(new RunTriggerCounters());
        var id = EffectInstanceId.Of("X");

        registry.Register(
            id,
            new EffectDefinition
            {
                Id = "X",
                Op = EffectOp.APPLY_STATUS,
                StatusId = "RAGE",
                Value = 0.1,
                Target = EffectTarget.SELF,
                Trigger = new EffectTrigger { Kind = TriggerKind.PERIODIC, Interval = 1.0 },
            },
            0);

        Should.Throw<EffectContextException>(() => registry.Evaluate(
                id, new TriggerOccurrence { Kind = TriggerKind.PERIODIC, Tick = 20 }))
            .Message.ShouldContain("PeriodicDue");
    }

    /// <summary><c>ON_DEATH</c> fires before removal — a Volatile-shaped enemy explodes on its own death.</summary>
    [Fact]
    public void ON_DEATH_fires_at_slot_6_before_the_actor_is_removed()
    {
        var fired = new List<string>();

        var result = CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10_000)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 10), effects: new HeldEffect(
                    new EffectDefinition
                    {
                        Id = "VOLATILE",
                        Op = EffectOp.APPLY_STATUS,
                        StatusId = "BURN",
                        Value = 0.15,
                        Target = EffectTarget.SELF,
                        Trigger = new EffectTrigger { Kind = TriggerKind.ON_DEATH },
                    })),
            },
            services => BattleSeams.Strict with
            {
                Attack = new RecordingAttackPipeline(services, damage: 10.0),
                Statuses = new CapturingStatusEngine(fired),
            }));

        fired.ShouldBe(new[] { "VOLATILE" });

        // The event follows the firing, in the same tick.
        result.Log.Single(e => e.Type == CombatEventType.ActorDeath).Tick.ShouldBe(0);
    }

    /// <summary>
    /// <c>ON_KILL</c> triggers never fire in duels, and the switch is
    /// <see cref="CombatRules.OnKillTriggersFire"/> rather than a branch in the swing slot.
    /// </summary>
    [Fact]
    public void ON_KILL_fires_in_PvE_and_not_when_the_rules_turn_it_off()
    {
        Kills(CombatRules.PvE).ShouldBe(new[] { "PK_MIDAS" });
        Kills(CombatRules.PvE with { OnKillTriggersFire = false }).ShouldBeEmpty();

        static List<string> Kills(CombatRules rules)
        {
            var fired = new List<string>();

            CombatSimulator.Simulate(BattleTestBench.Plan(
                new[]
                {
                    BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10_000), 1, new HeldEffect(
                        new EffectDefinition
                        {
                            Id = "PK_MIDAS",
                            Op = EffectOp.APPLY_STATUS,
                            StatusId = "RAGE",
                            Value = 0.1,
                            Target = EffectTarget.SELF,
                            Trigger = new EffectTrigger { Kind = TriggerKind.ON_KILL },
                        },
                        // An ON_KILL instance id is the RUN's, not a battle-local mint: its counter
                        // is run-scoped.
                        EffectInstanceId.Of("RUN#PERK_SLOT_1"))),
                    BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 10)),
                },
                services => BattleSeams.Strict with
                {
                    Attack = new RecordingAttackPipeline(services, damage: 10.0),
                    Statuses = new CapturingStatusEngine(fired),
                },
                rules: rules));

            return fired;
        }
    }

    /// <summary>
    /// An <c>ON_KILL</c> effect with no instance id would take a battle-local one and reset its
    /// counter every fight — refused instead.
    /// </summary>
    [Fact]
    public void An_ON_KILL_effect_without_a_run_owned_instance_id_is_refused()
    {
        var thrown = Should.Throw<ArgumentException>(() => CombatSimulator.Simulate(
            BattleTestBench.Plan(new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(), 1, new HeldEffect(new EffectDefinition
                {
                    Id = "PK_MIDAS",
                    Op = EffectOp.APPLY_STATUS,
                    StatusId = "RAGE",
                    Value = 0.1,
                    Target = EffectTarget.SELF,
                    Trigger = new EffectTrigger { Kind = TriggerKind.ON_KILL },
                })),
                BattleTestBench.Enemy(0),
            })));

        thrown.Message.ShouldContain("run-scoped");
    }

    /// <summary>
    /// <c>ON_BATTLE_END</c> fires after the loop breaks, at the fight's last tick, with
    /// <c>TriggerOccurrence.HeroWon</c> set from the outcome. The <c>onlyIfWon</c> arm is what makes
    /// this discriminating: a loop that fired the kind but left <c>HeroWon</c> unset would fire the
    /// loser's effect and skip the winner's.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ON_BATTLE_END_fires_at_the_last_tick_with_the_outcome_on_it(bool heroWins)
    {
        var fired = new List<string>();

        var result = CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(
                    BattleTestBench.Stats(maxHp: heroWins ? 10_000 : 10, aspd: heroWins ? 1.0 : 0.001),
                    1,
                    OnBattleEnd("A_ON_WIN", onlyIfWon: true),
                    OnBattleEnd("B_ALWAYS", onlyIfWon: null)),
                BattleTestBench.Enemy(
                    0, BattleTestBench.Stats(maxHp: heroWins ? 10 : 10_000)),
            },
            services => BattleSeams.Strict with
            {
                Attack = new RecordingAttackPipeline(services, damage: 10.0),
                Statuses = new CapturingStatusEngine(fired),
            }));

        result.HeroWon.ShouldBe(heroWins);

        fired.ShouldBe(heroWins
            ? new[] { "A_ON_WIN", "B_ALWAYS" }
            : new[] { "B_ALWAYS" });

        static HeldEffect OnBattleEnd(string id, bool? onlyIfWon) => new(new EffectDefinition
        {
            Id = id,
            Op = EffectOp.APPLY_STATUS,
            StatusId = "RAGE",
            Value = 0.1,
            Target = EffectTarget.SELF,
            Trigger = new EffectTrigger { Kind = TriggerKind.ON_BATTLE_END, OnlyIfWon = onlyIfWon },
        });
    }

    /// <summary>
    /// The phase check runs after every HP decrease, and a shrinking <c>MAX_HP</c> that clips
    /// current HP is one: a boss clipped below a phase threshold that way must enter the next phase
    /// there, not on whatever unrelated swing lands next.
    /// </summary>
    [Fact]
    public void A_MAX_HP_shrink_that_clips_current_HP_runs_the_phase_check()
    {
        RecordingPhases? phases = null;

        CombatSimulator.Simulate(BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10_000, aspd: 0.001)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 1000, aspd: 0.001), isBoss: true),
            },
            services =>
            {
                phases = new RecordingPhases(services);

                return BattleSeams.Strict with
                {
                    // Nobody deals damage: the ONLY HP decrease in the whole fight is the clip.
                    Attack = new RecordingAttackPipeline(services, damage: 0.0),
                    Timeline = new MaxHpRebaseAtTick(20, "ENEMY_0", fraction: -0.5),
                    Phases = phases,
                };
            },
            rules: new CombatRules(MaxTicks: 40, OnKillTriggersFire: true)));

        phases.ShouldNotBeNull();

        // The bucket lands in slot 1 of tick 20 and the aggregation that clips HP runs at the top of
        // tick 21, which is where the check is owed.
        phases.Calls.ShouldContain("check:ENEMY_0@21");

        // And it is the only check after tick 0 — the two on tick 0 are the opening swings, so
        // pinning the set rather than the count is what stops an unrelated call from satisfying the
        // assertion above.
        phases.Calls.Where(c => c.StartsWith("check:", StringComparison.Ordinal))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ShouldBe(new[] { "check:ENEMY_0@0", "check:ENEMY_0@21", "check:HERO@0" });
    }

    /// <summary>One registry per battle over the run's counters, so an <c>ON_KILL</c> counter persists across battles.</summary>
    [Fact]
    public void The_ON_KILL_counter_survives_a_battle_boundary_when_the_run_counters_are_reused()
    {
        var counters = new RunTriggerCounters();
        var fired = new List<string>();

        // Every 2nd kill. Two battles of one kill each: only the second battle's kill fires it.
        for (var battle = 0; battle < 2; battle++)
        {
            CombatSimulator.Simulate(new BattlePlan
            {
                BattleSeed = 7,
                Caps = SlayIdleRepeat.Core.Tests.Rules.Stats.StatFixtures.Caps(),
                Mitigation = SlayIdleRepeat.Core.Tests.Rules.Stats.StatFixtures.Mitigation(),
                WardCapPct = SlayIdleRepeat.Core.Tests.Rules.Stats.StatFixtures.WardCapPct,
                RunCounters = counters,
                Actors = new[]
                {
                    BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 10_000), 1, new HeldEffect(
                        new EffectDefinition
                        {
                            Id = "PK_MIDAS",
                            Op = EffectOp.APPLY_STATUS,
                            StatusId = "RAGE",
                            Value = 0.1,
                            Target = EffectTarget.SELF,
                            Trigger = new EffectTrigger { Kind = TriggerKind.ON_KILL, EveryNth = 2 },
                        },
                        EffectInstanceId.Of("RUN#PERK_SLOT_1"))),
                    BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 10)),
                },
                Seams = services => BattleSeams.Strict with
                {
                    Attack = new RecordingAttackPipeline(services, damage: 10.0),
                    Statuses = new CapturingStatusEngine(fired),
                },
            });
        }

        // Kill 1 counted and did not fire; kill 2, in the NEXT battle, did.
        fired.ShouldBe(new[] { "PK_MIDAS" });
    }
}

/// <summary>A slot-1 timeline that re-bases one actor's <c>MAX_HP</c> on a given tick, through the production path.</summary>
/// <remarks>
/// It writes a percent bucket and invalidates, rather than calling <c>SetStats</c> directly: the
/// aggregation is what clips current HP, and the phase check hangs off observing that clip. A double
/// that assigned the block itself would test nothing but itself.
/// </remarks>
internal sealed class MaxHpRebaseAtTick : IStatusTimeline
{
    private readonly int _tick;
    private readonly string _actorId;
    private readonly double _fraction;

    internal MaxHpRebaseAtTick(int tick, string actorId, double fraction)
    {
        _tick = tick;
        _actorId = actorId;
        _fraction = fraction;
    }

    /// <inheritdoc />
    public void AdvanceTimers(BattleActor actor, int tick)
    {
        if (tick != _tick || !string.Equals(actor.Id, _actorId, StringComparison.Ordinal))
        {
            return;
        }

        actor.Flow.AddPercentBucket(StatId.MAX_HP, _fraction);
        actor.InvalidateStats();
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

/// <summary>A status engine that records the tick each application landed on.</summary>
internal sealed class TickCapturingStatusEngine : IStatusEngine
{
    private readonly List<int> _ticks;
    private readonly BattleServices _services;

    internal TickCapturingStatusEngine(List<int> ticks, BattleServices services)
    {
        _ticks = ticks;
        _services = services;
    }

    /// <inheritdoc />
    public void Apply(
        IEffectActorView applier, IEffectActorView target, string statusId, double potency,
        EffectDuration? duration, EffectStacking? stacking, string sourceEffectId) =>
        _ticks.Add(_services.Tick);

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
