using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Combat;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;
using SlayIdleRepeat.Core.Rules.Stats;
using SlayIdleRepeat.Core.Tests.Rules.Stats;

namespace SlayIdleRepeat.Core.Tests.Rules.Combat;

/// <summary>
/// Drives `05` §4's <b>real</b> pipeline inside a real fight, at a point where `18` §8 has already
/// aggregated every actor.
/// </summary>
/// <remarks>
/// 🔒 Nothing here re-implements §4: the subject is <c>AttackPipeline</c> as <c>BattleSeams.For</c>
/// builds it, drawing from the fight's own <c>DeterministicRng</c> and writing into its own
/// <c>CombatLog</c>. What the bench adds is a <em>place to stand</em> — the probe runs from slot 1 at
/// tick 0, after the pre-tick's aggregation and before slot 4's swings.
/// <para>
/// 🔒 Basic attacks are off by default, which is what makes the draw counts assertable: slot 4a is gated
/// on <c>IStatusTimeline.CanAct</c>, so a timeline answering <c>false</c> stops every actor swinging
/// and the stream's <c>Position</c> counts the probe's draws and nothing else.
/// </para>
/// <para>
/// ⚠️ Caps default to <c>StatCaps.None</c>: a capped block can never be forced, and <c>NextDouble()</c>
/// is in <c>[0,1)</c>, so <c>1.0</c> is an always and <c>0.0</c> a never. The caps themselves are
/// asserted in <c>StatAggregationTests</c>.
/// </para>
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

    /// <summary>Every actor, in `05` §3.1 index order.</summary>
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
    /// <remarks>
    /// <c>BattleStart</c> and <c>BattleEnd</c> are the log's brackets (`05` §3.1 step 0d,
    /// <c>CombatLog.Complete</c>) and are asserted where they are the subject; a per-attack sequence
    /// assertion reads better without them.
    /// </remarks>
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
    /// <summary>
    /// Runs one fight, invoking <paramref name="body"/> from `05` §3.1 slot 1 of tick 0.
    /// </summary>
    /// <param name="actors">The roster.</param>
    /// <param name="body">The probe — the calls whose behaviour is the subject.</param>
    /// <param name="caps">`05` §1's ceilings. <c>StatCaps.None</c> by default; see the type remarks.</param>
    /// <param name="mitigation">`05` §4's 📐 dials. The shipped pair by default.</param>
    /// <param name="wardCapPct">`05` §4.1's 📐 pool ceiling. The shipped 1.0 by default.</param>
    /// <param name="battleSeed">`14` §8.1's battle seed.</param>
    /// <param name="maxTicks">How long the fight may run. One tick is enough for most probes.</param>
    /// <param name="actorsMaySwing">Whether `05` §3.1 slot 4 runs. Off by default; see the type remarks.</param>
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
    /// 🔒 A `05` §1 block with the named stats set, <c>ASPD</c> and <c>HEAL_PCT</c> at their `05` §2
    /// bases, and every other stat at zero.
    /// </summary>
    /// <remarks>
    /// 🔒 <b><c>HEAL_PCT</c> is 1.0 rather than 0, and it is load-bearing.</b>
    /// <c>StatFixtures.Block</c> zeroes what it is not given, and `05` §2 is explicit that
    /// <c>HEAL%</c>'s base is 1.0 <em>"so that lifesteal and heals work with no modifiers"</em>. A
    /// zero here would make every lifesteal and heal case in the suite pass by healing nothing —
    /// which is why the default is stated once, here, rather than in each test class.
    /// </remarks>
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
    /// An effect with a condition — the shape that makes `18` §8 re-aggregate its holder on every
    /// tick (<c>BattleActor.StatsDependOnLiveState</c>).
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

    /// <summary>A standing (untriggered) stat effect — `18` §8 step 1 collects it every pass.</summary>
    internal static HeldEffect Standing(string id, EffectOp op, StatSelector stat, double value) =>
        new(new EffectDefinition { Id = id, Op = op, Stat = stat, Value = value });

    /// <summary>
    /// 🔒 `05` §3.1's <c>SYS_ENRAGE</c> shape as a <b>standing conditional</b>: a <c>STAT_MULT</c> that
    /// switches on at <paramref name="afterSeconds"/> of battle time.
    /// </summary>
    /// <remarks>
    /// ⚠️ The real <c>SYS_ENRAGE</c> is a <c>PERIODIC</c> and is not usable here: `18` §8 step 1
    /// aggregates <em>untriggered</em> effects only, so a <c>PERIODIC STAT_MULT</c> moves no stat on
    /// this branch and would prove nothing. What the ward-cap claim needs is a post-step-7 Max HP that
    /// <b>changes mid-fight</b>, and a `18` §4 condition is the one mechanism that delivers it —
    /// <c>RefreshStats</c> re-aggregates a state-dependent holder every tick.
    /// <para>
    /// ⚠️ <c>MAX_HP</c> rather than the enrage's <c>ATK</c>: the ward cap reads Max HP, and a growing ATK
    /// would prove the re-read of a number the cap does not consult.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// The timeline the bench runs on: it fires the probe once, at `05` §3.1 slot 1 of tick 0, and
    /// answers slot 4a's <em>"and not stunned"</em>.
    /// </summary>
    private sealed class ProbeTimeline : IStatusTimeline
    {
        private readonly Action _body;
        private readonly bool _mayAct;

        /// <inheritdoc />
        /// <remarks>
        /// Added by the conductor at merge. M2-10 appended <c>StatModifiers</c> as
        /// <see cref="IStatusTimeline"/>'s fifth member and patched every double on its own branch;
        /// this one lives on M2-09's, which ran in parallel. Empty is the honest answer rather than a
        /// stub — this probe exists to drive `05` §4's pipeline and slot 4a's stun gate, and carries
        /// no statuses, so anything else would be an invention.
        /// </remarks>
        public IReadOnlyList<EffectDefinition> StatModifiers(BattleActor actor) => [];

        private int _lastTick = -1;

        internal ProbeTimeline(Action body, bool mayAct)
        {
            _body = body;
            _mayAct = mayAct;
        }

        /// <summary>
        /// Fires the probe <b>once per tick</b>, on the first actor of `05` §3.1 slot 1.
        /// </summary>
        /// <remarks>
        /// Once per tick rather than once per fight, so that a case whose subject changes over time —
        /// the ward cap under a growing Max HP — can observe the same reading twice and branch on
        /// <c>BattleServices.Tick</c>. A one-tick fight, which is what most cases run, is unaffected.
        /// </remarks>
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
