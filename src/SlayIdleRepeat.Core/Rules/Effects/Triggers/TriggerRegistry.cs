using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// Every live effect instance's trigger state for one battle, and the two questions the tick loop
/// asks of them: does this instance fire on this moment, and which <c>PERIODIC</c>s are due on this tick.
/// </summary>
/// <remarks>
/// <para>
/// This type owns the trigger model, the registry and the firing predicates. The tick loop owns the
/// clock and plugs itself in — nothing here assumes it owns the clock: there is no timer, no
/// <c>while</c>, and every method takes the tick as an argument.
/// </para>
/// <para>The wiring contract for whoever drives the tick loop:</para>
/// <list type="number">
///   <item>One registry per battle, constructed with the run's <see cref="IRunTriggerCounters"/> —
///   the run's, held across battles, not rebuilt per fight. That single choice is what makes
///   "<c>ON_ATTACK</c> counters reset at battle start; <c>ON_KILL</c> counters persist across
///   battles" true structurally rather than by a step somebody remembers.</item>
///   <item><see cref="Register"/> every effect active at battle start with
///   <c>activationTick: 0</c>, then <see cref="Evaluate"/> each with an <c>ON_BATTLE_START</c>
///   occurrence, hero side first and in <see cref="EffectOrder"/> order within an actor. The boss's
///   phase 1 counts as entered too: register its phase-1 block at tick 0 and evaluate an
///   <c>ON_PHASE_ENTER</c> occurrence with <c>Phase = 1</c>.</item>
///   <item>Every tick, call <see cref="PeriodicDue"/> once per actor in actor order, handing it that
///   actor's instance ids — the only <c>PERIODIC</c> path; <see cref="Evaluate"/> refuses the kind.</item>
///   <item>Per swing: an <c>ON_ATTACK</c> occurrence, then <c>ON_HIT</c> / <c>ON_CRIT</c> /
///   <c>ON_HIT_TAKEN</c> / <c>ON_DODGE</c> / <c>ON_BLOCK</c> / <c>ON_KILL</c> as combat resolves
///   them, depth-first, in <see cref="EffectOrder"/> order. Hand in the battle's combat draw stream
///   so <c>chance</c> can be drawn. Use <see cref="EvaluateAll"/> for a flat sweep; drive a cascade
///   that changes the roster one instance at a time through <see cref="Evaluate"/>.</item>
///   <item>A pet's active ability carries no trigger of its own — its cadence is the wrapper's
///   cooldown — so it only raises the <c>ON_HIT</c> family, through the damage it resolves.</item>
///   <item><c>ON_DEATH</c> fires before removal. Nothing here filters on liveness — liveness filters
///   selection, not naming — which is what lets a death explosion fire on the actor that just died.</item>
///   <item>On each phase entry, register that phase's block at the current tick and
///   <see cref="Deactivate"/> the exited phase's scoped instances. A burst that crosses two phase
///   thresholds does this twice in one tick, in order; an instance already live does not re-anchor.</item>
///   <item>After a firing, ask <see cref="TriggerRouting"/> what to do with the effect, passing
///   <see cref="TriggerLayer.COMBAT"/> as the firing layer — a combat trigger carrying a run/board op
///   is emitted, never resolved.</item>
///   <item>The six run-layer kinds (<c>ON_TILE_RESOLVED</c>, <c>ON_ROLL</c>, <c>ON_PERK_TAKEN</c>,
///   <c>ON_STAGE_GATE</c>, <c>ON_RUN_START</c>, <c>ON_RUN_END</c>) are declared and unit-tested here
///   but fired by the run controller, not the tick loop — this registry will answer them if asked,
///   since the predicate is the same one.</item>
/// </list>
/// <para>Six combat kinds have no numbered slot of their own:</para>
/// <list type="bullet">
///   <item><c>ALWAYS</c> — no moment at all; re-evaluated at every resolution pass rather than fired
///   by an event. This registry answers <c>FIRES</c> for a live one if asked.</item>
///   <item><c>ON_BATTLE_END</c> — at the fight's last tick, with <see cref="TriggerOccurrence.HeroWon"/>
///   set from the outcome; the only kind that reads that field. Applied after the run queue and
///   before <c>ON_BATTLE_END</c> effects are granted.</item>
///   <item><c>ON_LOW_HP</c> — after every HP change of the holder, DoT ticks included. It's a
///   crossing, so a skipped observation is a firing lost — hand in the post-change fraction and let
///   the instance hold the previous one.</item>
///   <item><c>ON_LETHAL</c> — when a hit would be fatal, before removal. The anti-loop bound on
///   <c>SURVIVE_LETHAL</c>/<c>REVIVE</c> is a rule about the op, applied by the caller;
///   <see cref="TriggerInstance.FireCount"/> is what it reads.</item>
///   <item><c>ON_HEAL</c> — for every heal: lifesteal, HoT cadence, a <c>HEAL</c> op. Fired after
///   the HP is applied, which is what makes <c>HEAL_AMOUNT</c> and <c>OVERHEAL_AMOUNT</c> readable.</item>
///   <item><c>ON_REVIVE</c> — when the <c>REVIVE</c> op or an ad revive returns the actor from 0 HP.
///   <c>SURVIVE_LETHAL</c> does not count, since the actor never died.</item>
/// </list>
/// <para>
/// Two things this registry deliberately does not know: it's actor-blind (a
/// <see cref="TriggerInstance"/> carries no actor; the actor-to-instances map is the caller's to
/// maintain), and it does not mint <see cref="EffectInstanceId"/>s — <see cref="Register"/> refuses a
/// duplicate, which is the per-instance rule's teeth, but the minting rule itself (a run-scoped
/// holding gets a stable id, a battle-local effect gets a battle-local one) is the caller's to get right.
/// </para>
/// <para>What this class does not do: order actors, select targets, evaluate conditions, apply ops, or write to the combat log. A trigger says when; the rest says what, to whom, how much and for how long.</para>
/// <para>Not thread-safe: one battle, one caller.</para>
/// </remarks>
internal sealed class TriggerRegistry
{
    private readonly Dictionary<EffectInstanceId, TriggerInstance> _instances = new();
    private readonly IRunTriggerCounters _runCounters;

    private int _lastPeriodicTick;

    /// <summary>Builds a registry for one battle.</summary>
    /// <param name="runCounters">The run's counter store, not a per-battle one. See the type remarks.</param>
    internal TriggerRegistry(IRunTriggerCounters runCounters)
    {
        ArgumentNullException.ThrowIfNull(runCounters);

        _runCounters = runCounters;
    }

    /// <summary>Every instance registered, in ascending ordinal instance-id order.</summary>
    /// <remarks>Ordered here for <see cref="RunTriggerCounters.Entries"/>' reason.</remarks>
    internal IReadOnlyList<TriggerInstance> Instances() =>
        _instances.Values.OrderBy(i => i.Id.Value, EffectInstanceId.Comparer).ToArray();

    /// <summary>Registers and activates one effect instance — and, for a <c>PERIODIC</c>, starts its clock at <paramref name="activationTick"/>.</summary>
    /// <param name="id">The instance's stable id. See <see cref="EffectInstanceId"/>.</param>
    /// <param name="effect">The authored effect.</param>
    /// <param name="activationTick">The tick the effect becomes active on.</param>
    /// <param name="holderHpFraction">The holder's HP fraction now. Required for <c>ON_LOW_HP</c> and ignored otherwise.</param>
    /// <exception cref="EffectContextException">
    /// The id is already registered, the effect carries no trigger, the trigger is malformed, or an
    /// <c>ON_LOW_HP</c> was registered without an HP reading.
    /// </exception>
    /// <remarks>
    /// A duplicate id is refused, and that refusal is the per-instance rule's teeth: counters live on
    /// the effect instance, so a caller holding two copies of one effect must name them differently —
    /// a registry that quietly merged them would give both copies one shared counter.
    /// </remarks>
    internal TriggerInstance Register(
        EffectInstanceId id,
        EffectDefinition effect,
        int activationTick,
        double? holderHpFraction = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentOutOfRangeException.ThrowIfNegative(activationTick);

        // Checked here and not left to EffectInstanceId.Of: default(EffectInstanceId) is a perfectly
        // good dictionary key, so an unnamed instance would otherwise register cleanly and share its
        // counter with every other unnamed instance.
        if (!id.NamesAHolding)
        {
            throw new EffectContextException(
                effect.Id,
                "it was registered under an instance id that names no holding",
                "`18` §3's everyNth counters live on the effect INSTANCE. An unnamed instance shares " +
                "one counter with every other unnamed instance, and an ON_KILL counter keyed on " +
                "nothing cannot survive to the next battle.");
        }

        if (_instances.ContainsKey(id))
        {
            throw new EffectContextException(
                id.Value ?? string.Empty,
                "an instance with that id is already registered in this battle",
                "`18` §3's everyNth counters live on the effect INSTANCE, so two copies of one " +
                "effect count separately. Merging them onto one id would share their counters and " +
                "halve how often the second copy fires.");
        }

        var instance = new TriggerInstance(id, effect, _runCounters, activationTick, holderHpFraction);
        _instances[id] = instance;

        return instance;
    }

    /// <summary>The registered instance with that id.</summary>
    /// <param name="id">The instance id.</param>
    /// <exception cref="EffectContextException">Nothing is registered under that id.</exception>
    internal TriggerInstance this[EffectInstanceId id] =>
        _instances.TryGetValue(id, out var instance)
            ? instance
            : throw new EffectContextException(
                id.Value ?? string.Empty,
                "no effect instance is registered under that id in this battle",
                "Every instance the tick loop evaluates is registered when its effect becomes " +
                "active (`05` §3.1 steps 0b and 0c, and each phase entry). Answering 'does not " +
                "fire' for an unregistered id would make a wiring gap indistinguishable from a " +
                "trigger that never came up.");

    /// <summary>Whether an instance is registered under that id.</summary>
    /// <param name="id">The instance id.</param>
    internal bool IsRegistered(EffectInstanceId id) => _instances.ContainsKey(id);

    /// <summary>Ends an instance — a <c>PHASE</c> scope at a phase exit, or any other removal.</summary>
    /// <param name="id">The instance id.</param>
    internal void Deactivate(EffectInstanceId id) => this[id].Deactivate();

    /// <summary>Re-activates an instance that was deactivated — a re-grant. Its clock and its <c>ON_LOW_HP</c> arming both restart from <paramref name="tick"/>; a live instance is untouched.</summary>
    /// <param name="id">The instance id.</param>
    /// <param name="tick">The tick the effect becomes active again on.</param>
    /// <param name="holderHpFraction">The holder's HP fraction now. Required for <c>ON_LOW_HP</c> and ignored otherwise.</param>
    /// <remarks>
    /// Distinct from <see cref="Register"/>, which refuses a duplicate id. Boss phases never revert,
    /// so this isn't on the phase path — it's for the cases that do repeat: a summon re-entering, an
    /// ad revive, or a re-grant of an expired effect.
    /// </remarks>
    internal void Activate(EffectInstanceId id, int tick, double? holderHpFraction = null) =>
        this[id].Activate(tick, holderHpFraction);

    /// <summary>Whether an instance fires on a moment, and which rule decided.</summary>
    /// <param name="id">The instance id.</param>
    /// <param name="occurrence">The moment.</param>
    /// <param name="rng">The battle's combat draw stream, for <c>chance</c>.</param>
    /// <exception cref="EffectContextException">The id is unregistered, or the occurrence is <c>PERIODIC</c> — see the remarks.</exception>
    /// <remarks>
    /// <c>PERIODIC</c> is refused here: it's the one kind whose firing is decided by a schedule
    /// rather than a moment, and evaluating it through this path would advance that schedule from
    /// whatever tick the caller happened to ask on. <see cref="PeriodicDue"/> is the single path.
    /// </remarks>
    internal TriggerOutcome Evaluate(EffectInstanceId id, in TriggerOccurrence occurrence, DeterministicRng? rng = null)
    {
        if (occurrence.Kind == TriggerKind.PERIODIC)
        {
            throw new EffectContextException(
                TriggerKind.PERIODIC.ToString(),
                $"a PERIODIC occurrence was evaluated through {nameof(Evaluate)}",
                $"A PERIODIC fires on a schedule anchored when its effect became active (R8), not on " +
                $"a moment. Use {nameof(PeriodicDue)}, which is the one path that advances that " +
                "schedule — `05` §3.1 slot 3.");
        }

        return this[id].Evaluate(occurrence, rng);
    }

    /// <summary>The same question over several instances, in ascending effect-id order.</summary>
    /// <param name="candidates">The instance ids to ask — one actor's, so the caller can walk actors in the right order.</param>
    /// <param name="occurrence">The moment.</param>
    /// <param name="rng">The battle's combat draw stream.</param>
    /// <returns>The instances that fired, in the order they were decided.</returns>
    /// <remarks>
    /// <para>
    /// Exists so "ascending effect-id order" isn't something every call site has to remember —
    /// <see cref="PeriodicDue"/> already imposes it for the periodic path, and leaving the rest to
    /// the caller would risk a bare <c>OrderBy(e =&gt; e.Id)</c> that's culture-dependent.
    /// </para>
    /// <para>
    /// Not for a cascade that changes the roster: on-hit triggers resolve immediately, depth-first,
    /// so if firing one instance can register another or kill the holder, the tick loop must drive
    /// them one at a time through <see cref="Evaluate"/> instead. This is the flat case — every
    /// candidate decided against one unchanging moment.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<TriggerInstance> EvaluateAll(
        IEnumerable<EffectInstanceId> candidates,
        in TriggerOccurrence occurrence,
        DeterministicRng? rng = null)
    {
        var ordered = Ordered(candidates);
        var fired = new List<TriggerInstance>(ordered.Count);

        foreach (var instance in ordered)
        {
            if (instance.Evaluate(occurrence, rng) == TriggerOutcome.FIRES)
            {
                fired.Add(instance);
            }
        }

        return fired;
    }

    /// <summary>The <c>PERIODIC</c> instances among <paramref name="candidates"/> that fire on <paramref name="tick"/>, in ascending effect-id order, with their schedules advanced.</summary>
    /// <param name="candidates">The instance ids to consider — one actor's. An id that is not a <c>PERIODIC</c>, or is inactive, is simply not due.</param>
    /// <param name="tick">The tick being run.</param>
    /// <remarks>
    /// <para>
    /// The order is imposed here, not assumed: a caller cannot change which ward lands first by
    /// reordering the list it hands in, and a bare culture-aware sort would put a German phone and a
    /// Linux container in different orders.
    /// </para>
    /// <para>Calling it twice for one tick is safe and returns nothing the second time, because a firing advances the instance past the tick — the property that makes this the single path rather than a convenience over <see cref="Evaluate"/>.</para>
    /// <para>Ticks never go backwards: a tick below a previous call throws rather than silently answering <c>NOT_DUE</c> for everything and losing every firing in between.</para>
    /// </remarks>
    internal IReadOnlyList<TriggerInstance> PeriodicDue(IEnumerable<EffectInstanceId> candidates, int tick)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        if (tick < _lastPeriodicTick)
        {
            throw new EffectContextException(
                nameof(PeriodicDue),
                $"it was asked for tick {tick.ToString(CultureInfo.InvariantCulture)} after tick " +
                $"{_lastPeriodicTick.ToString(CultureInfo.InvariantCulture)}",
                "`05` §3.1's loop runs ticks in ascending order. A tick that goes backwards answers " +
                "NOT_DUE for everything, so every firing in between would be lost silently.");
        }

        _lastPeriodicTick = tick;

        var ordered = Ordered(candidates);
        var due = new List<TriggerInstance>(ordered.Count);

        foreach (var instance in ordered)
        {
            if (instance.EvaluatePeriodic(tick) == TriggerOutcome.FIRES)
            {
                due.Add(instance);
            }
        }

        return due;
    }

    /// <summary>The candidates as instances, in ascending effect-id order — resolved and ordered before any of them fires.</summary>
    /// <remarks>
    /// <para>
    /// Materialised first, so an unregistered id throws before any instance has been advanced and
    /// the registry can't be left half-fired.
    /// </para>
    /// <para>
    /// The tie-break is the instance id, and it's load-bearing: the primary key is the effect id, and
    /// two copies of one effect on one actor tie on it, so a stable sort would let the caller's list
    /// order decide which one resolves first.
    /// </para>
    /// <para>A duplicate id in one call is refused: a schedule that's behind can leave an instance due again immediately, so a duplicate would fire twice inside one tick.</para>
    /// </remarks>
    private List<TriggerInstance> Ordered(IEnumerable<EffectInstanceId> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var seen = new HashSet<EffectInstanceId>();
        var instances = new List<TriggerInstance>();

        foreach (var id in candidates)
        {
            if (!seen.Add(id))
            {
                throw new EffectContextException(
                    id.Value ?? string.Empty,
                    "it appears twice among the candidates of one call",
                    "One instance decides once per moment. A repeated id fires twice inside one tick " +
                    "whenever its schedule is behind, which no combat log would explain.");
            }

            instances.Add(this[id]);
        }

        instances.Sort(ByEffectThenInstanceId);

        return instances;
    }

    /// <summary>Ordinal effect-id order, tie-broken by the instance id.</summary>
    private static int ByEffectThenInstanceId(TriggerInstance left, TriggerInstance right)
    {
        var byEffect = EffectOrder.IdComparer.Compare(left.Effect.Id, right.Effect.Id);

        return byEffect != 0
            ? byEffect
            : EffectInstanceId.Comparer.Compare(left.Id.Value, right.Id.Value);
    }
}
