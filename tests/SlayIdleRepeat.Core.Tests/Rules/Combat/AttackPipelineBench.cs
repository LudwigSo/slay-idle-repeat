using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>Drives the real attack pipeline inside a real fight, at a point where every actor is already aggregated.</summary>
/// <remarks>
/// Nothing here re-implements the pipeline: the subject is <c>AttackPipeline</c> as
/// <c>BattleSeams.For</c> builds it, drawing from the fight's own <c>DeterministicRng</c> and writing
/// into its own <c>CombatLog</c>. Basic attacks are off by default, which is what makes the draw
/// counts assertable: a timeline answering <c>false</c> to <c>CanAct</c> stops every actor swinging,
/// so the stream's <c>Position</c> counts the probe's draws and nothing else. Caps default to
/// <c>StatCaps.None</c> so <c>1.0</c> is always and <c>0.0</c> is never for any stat under test.
/// </remarks>
internal sealed class AttackProbe
{
    private BattleServices? _services;
    private AttackPipeline? _pipeline;

    /// <summary>The pipeline under test — the real one, built by <c>BattleSeams.For</c>'s constructor.</summary>
    internal AttackPipeline Pipeline =>
        _pipeline ?? throw new InvalidOperationException("The probe body ran before the seams were built.");

    /// <summary>This fight's services — its log, its draw stream, its dials.</summary>
    internal BattleServices Services =>
        _services ?? throw new InvalidOperationException("The probe body ran before the seams were built.");

    /// <summary>Every actor, in index order.</summary>
    internal IReadOnlyList<BattleActor> Actors => Services.Actors;

    /// <summary>The hero — roster index 0.</summary>
    internal BattleActor Hero => Actors[0];

    /// <summary>The enemy at <paramref name="index"/> in the enemy list.</summary>
    internal BattleActor Enemy(int index = 0) =>
        Actors.First(a => a.Index == CombatActor.FirstEnemy + index);

    /// <summary>The finished fight. Set once <see cref="AttackPipelineBench.Run"/> returns.</summary>
    internal SimulationResult Result { get; private set; } = null!;

    /// <summary>The whole log, sealed.</summary>
    internal IReadOnlyList<CombatEvent> Events => Result.Log;

    /// <summary>Every event of one type, in emission order.</summary>
    internal IReadOnlyList<CombatEvent> EventsOf(CombatEventType type) =>
        Result.Log.Where(e => e.Type == type).ToArray();

    /// <summary>The log's types in emission order, with the frame events dropped.</summary>
    internal IReadOnlyList<CombatEventType> Sequence() =>
        Result.Log
            .Where(e => e.Type is not (CombatEventType.BattleStart or CombatEventType.BattleEnd))
            .Select(e => e.Type)
            .ToArray();

    internal void Bind(BattleServices services, AttackPipeline pipeline)
    {
        _services = services;
        _pipeline = pipeline;
    }

    internal void Finish(SimulationResult result) => Result = result;
}

/// <summary>Builds and runs the probe fight. See <see cref="AttackProbe"/> for the design.</summary>
internal static class AttackPipelineBench
{
    /// <summary>Runs one fight, invoking <paramref name="body"/> from slot 1 of tick 0.</summary>
    /// <param name="actors">The roster.</param>
    /// <param name="body">The probe — the calls whose behaviour is the subject.</param>
    /// <param name="caps">Stat ceilings. <c>StatCaps.None</c> by default; see the type remarks.</param>
    /// <param name="mitigation">Mitigation dials. The shipped pair by default.</param>
    /// <param name="wardCapPct">Ward pool ceiling. The shipped 1.0 by default.</param>
    /// <param name="battleSeed">The battle seed.</param>
    /// <param name="maxTicks">How long the fight may run. One tick is enough for most probes.</param>
    /// <param name="actorsMaySwing">Whether basic attacks run. Off by default; see the type remarks.</param>
    internal static AttackProbe Run(
        IEnumerable<ActorPlan> actors,
        Action<AttackProbe> body,
        StatCaps? caps = null,
        MitigationConstants? mitigation = null,
        double wardCapPct = StatFixtures.WardCapPct,
        ulong battleSeed = 0xC0FFEE_1234_5678UL,
        int maxTicks = 1,
        bool actorsMaySwing = false)
    {
        var probe = new AttackProbe();

        var plan = new BattlePlan
        {
            BattleSeed = battleSeed,
            Actors = actors.ToArray(),
            Caps = caps ?? StatCaps.None,
            Mitigation = mitigation ?? StatFixtures.Mitigation(),
            WardCapPct = wardCapPct,
            Rules = new CombatRules(maxTicks, OnKillTriggersFire: true),
            RunCounters = new RunTriggerCounters(),
            Seams = services =>
            {
                probe.Bind(services, new AttackPipeline(services));

                return BattleSeams.Strict with
                {
                    Attack = probe.Pipeline,
                    Timeline = new ProbeTimeline(() => body(probe), actorsMaySwing),
                };
            },
        };

        probe.Finish(CombatSimulator.Simulate(plan));

        return probe;
    }

    /// <summary>
    /// A stat block with the named stats set, <c>ASPD</c> and <c>HEAL_PCT</c> at their bases, and
    /// every other stat at zero. <c>HEAL_PCT</c> is 1.0 rather than 0 and is load-bearing: a zero
    /// here would make every lifesteal and heal case in the suite pass by healing nothing.
    /// </summary>
    internal static ActorStats Stats(double maxHp, params (StatId Stat, double Value)[] rest)
    {
        var values = new List<(StatId, double)>
        {
            (StatId.MAX_HP, maxHp),
            (StatId.ASPD, 1.0),
            (StatId.HEAL_PCT, 1.0),
        };

        values.AddRange(rest.Select(r => (r.Stat, r.Value)));

        return StatFixtures.Block(values.ToArray());
    }

    /// <summary>
    /// An effect with a condition — the shape that makes the holder re-aggregate on every tick
    /// (<c>BattleActor.StatsDependOnLiveState</c>).
    /// </summary>
    internal static HeldEffect Conditional(
        string id, EffectOp op, StatId stat, double value, EffectCondition condition) =>
        new(new EffectDefinition
        {
            Id = id,
            Op = op,
            Stat = StatSelector.Of(stat),
            Value = value,
            Condition = condition,
        });

    /// <summary>A standing (untriggered) stat effect — aggregation collects it every pass.</summary>
    internal static HeldEffect Standing(string id, EffectOp op, StatSelector stat, double value) =>
        new(new EffectDefinition { Id = id, Op = op, Stat = stat, Value = value });

    /// <summary>
    /// An enrage shape as a standing conditional: a <c>STAT_MULT</c> that switches on at
    /// <paramref name="afterSeconds"/> of battle time. The real enrage effect is <c>PERIODIC</c> and
    /// is not usable here, since aggregation only collects untriggered effects; a condition is the
    /// mechanism that delivers a Max HP that changes mid-fight instead.
    /// </summary>
    internal static HeldEffect EnrageShaped(string id, StatId stat, double multiplier, double afterSeconds) =>
        Conditional(
            id,
            EffectOp.STAT_MULT,
            stat,
            multiplier,
            EffectCondition.Of(new ConditionTerm
            {
                Fn = ConditionFunction.BATTLE_TIME,
                Comparator = ConditionComparator.GTE,
                Value = afterSeconds,
            }));

    /// <summary>The timeline the bench runs on: it fires the probe once per tick and answers the stun gate.</summary>
    private sealed class ProbeTimeline : IStatusTimeline
    {
        private readonly Action _body;
        private readonly bool _mayAct;

        /// <inheritdoc />
        /// <remarks>
        /// Empty is the honest answer rather than a stub — this probe exists to drive the attack
        /// pipeline and the stun gate, and carries no statuses.
        /// </remarks>
        public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor) => [];

        private int _lastTick = -1;

        internal ProbeTimeline(Action body, bool mayAct)
        {
            _body = body;
            _mayAct = mayAct;
        }

        /// <summary>
        /// Fires the probe once per tick, so a case whose subject changes over time — the ward cap
        /// under a growing Max HP — can observe the same reading twice and branch on
        /// <c>BattleServices.Tick</c>.
        /// </summary>
        public void AdvanceTimers(BattleActor actor, int tick)
        {
            if (tick == _lastTick)
            {
                return;
            }

            _lastTick = tick;
            _body();
        }

        public void ExpireDue(BattleActor actor, int tick)
        {
        }

        public bool CanAct(BattleActor actor) => _mayAct;

        public int StacksOn(BattleActor actor, string statusId) => 0;
    }
}
