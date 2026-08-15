using Shouldly;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Combat.Status;
using SlayIdleRepeat.Core.Rules.Stats;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat.Status;

/// <summary>A DoT tick is a damage event, not an attack, and therefore takes no draw.</summary>
/// <remarks>
/// The only three draws in a normal attack are dodge, crit and block, and a DoT tick is exempt
/// from all three. So the combat stream must be in exactly the same position after a fight full of
/// DoTs as after the same fight with none — otherwise two runs of one battle diverge on the first
/// attack after the first DoT.
/// <para>
/// The assertion is on <c>DeterministicRng.Position</c>, not the log: two fights can produce
/// identical logs while one of them drew, because a draw whose result was never used changes
/// nothing visible until the next draw that is.
/// </para>
/// </remarks>
public sealed class StatusDeterminismTests
{
    /// <summary>
    /// A fight in which every kind of status lands and ticks consumes the same number of draws as
    /// the identical fight with no statuses at all.
    /// </summary>
    [Fact]
    public void A_fight_full_of_status_ticks_takes_exactly_the_draws_a_fight_with_none_takes()
    {
        var withStatuses = DrawsTaken(withStatuses: true);
        var without = DrawsTaken(withStatuses: false);

        withStatuses.Ticks.ShouldBeGreaterThan(20,
            "a fight in which nothing ticked would satisfy the draw assertion trivially");

        withStatuses.Position.ShouldBe(without.Position);
    }

    /// <summary>
    /// The stream this suite watches is the one the fight actually uses — the floor under the rule
    /// above.
    /// </summary>
    /// <remarks>
    /// If the simulation drew nothing at all, <c>Position</c> would be <c>0</c> on both sides and
    /// the rule would pass over a stream nothing touches. It is asserted non-zero so the comparison
    /// is between two real numbers.
    /// </remarks>
    [Fact]
    public void The_combat_stream_is_drawn_from_at_all()
    {
        DrawsTaken(withStatuses: false).Position.ShouldBeGreaterThan(0UL);
    }

    private static (ulong Position, int Ticks) DrawsTaken(bool withStatuses)
    {
        StatusTimeline? timeline = null;
        RecordingStatusPipeline? pipeline = null;
        BattleSimulation? simulation = null;

        var plan = BattleTestBench.Plan(
            new[]
            {
                BattleTestBench.Hero(BattleTestBench.Stats(maxHp: 100_000, atk: 20.0)),
                BattleTestBench.Enemy(0, BattleTestBench.Stats(maxHp: 100_000, atk: 10.0)),
            },
            services =>
            {
                pipeline = new DrawingPipeline(services);
                timeline = new StatusTimeline(services, pipeline, StatusFixtures.Catalogue());

                return BattleSeams.Strict with
                {
                    Attack = pipeline,
                    Statuses = timeline,
                    Timeline = withStatuses
                        ? new EveryStatusAtTick7(timeline, () => simulation!)
                        : timeline,
                };
            },
            rules: new CombatRules(400, OnKillTriggersFire: true));

        simulation = new BattleSimulation(plan);
        simulation.Run();

        return (simulation.Rng.Position, pipeline!.Dots.Count + pipeline.Heals.Count);
    }

    /// <summary>
    /// A pipeline that draws once per attack, as the dodge roll does — so the stream moves for
    /// attacks and only for attacks.
    /// </summary>
    private sealed class DrawingPipeline : RecordingStatusPipeline
    {
        private readonly BattleServices _services;

        internal DrawingPipeline(BattleServices services)
            : base(services) => _services = services;

        internal override void OnAttack() => _ = _services.Rng.NextDouble();
    }

    /// <summary>Applies all cadence-driven statuses at tick 7, then delegates.</summary>
    /// <remarks>
    /// The ticking statuses and no others, and the restriction is the point: the claim under test
    /// is that the cadence path takes no draw. <c>STUN</c>, <c>FREEZE</c> and <c>HASTE</c>
    /// legitimately change how many attacks a fight contains — a stunned enemy does not swing — and
    /// each attack does draw, so including them would make the two fights differ for a reason that
    /// has nothing to do with the cadence. The catalogue's own <c>Ticks</c> predicate selects them,
    /// so a fifth ticking status is covered without an edit here.
    /// </remarks>
    private sealed class EveryStatusAtTick7 : IStatusTimeline
    {
        private readonly StatusTimeline _inner;
        private readonly Func<BattleSimulation> _simulation;

        internal EveryStatusAtTick7(StatusTimeline inner, Func<BattleSimulation> simulation)
        {
            _inner = inner;
            _simulation = simulation;
        }

        public void AdvanceTimers(BattleActor actor, int tick)
        {
            if (tick == 7 && actor.Index == 0)
            {
                var actors = _simulation().Actors;
                var hero = actors.Single(a => a.Id == "HERO");
                var enemy = actors.Single(a => a.Id == "ENEMY_0");

                var ticking = StatusFixtures.Catalogue().Statuses
                    .Where(s => s.Ticks)
                    .Select(s => s.Id);

                foreach (var status in ticking)
                {
                    _inner.Apply(
                        hero, enemy, status, 0.05,
                        new EffectDuration { Scope = DurationScope.BATTLE, Seconds = 30.0 },
                        stacking: null, $"E_{status}");
                }
            }

            _inner.AdvanceTimers(actor, tick);
        }

        public void ExpireDue(BattleActor actor, int tick) => _inner.ExpireDue(actor, tick);

        public bool CanAct(BattleActor actor) => _inner.CanAct(actor);

        public int StacksOn(BattleActor actor, string statusId) => _inner.StacksOn(actor, statusId);

        public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor) =>
            _inner.StatModifiers(actor);
    }
}
