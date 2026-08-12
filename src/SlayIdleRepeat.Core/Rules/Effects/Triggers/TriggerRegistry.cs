using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rng;

namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// 🔒 Every live effect instance's trigger state for one battle, and the two questions `05` §3.1's
/// tick loop asks of them: <em>does this instance fire on this moment</em>, and <em>which
/// <c>PERIODIC</c>s are due on this tick</em>.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>THE WIRING CONTRACT — WHAT M2-08 MUST DO</b> ═══
/// </para>
/// <para>
/// This task owns the trigger model, the registry and the firing predicates, tested against a
/// hand-driven tick source. <b>M2-08 owns the real tick loop</b> and plugs it in. Nothing here
/// assumes it owns the clock: there is no timer, no <c>while</c>, and every method takes the tick as
/// an argument.
/// </para>
/// <list type="number">
///   <item>
///     <b>One registry per battle.</b> Construct it with the run's
///     <see cref="IRunTriggerCounters"/> — the <em>run's</em>, held across battles, not one built
///     per fight. That single choice is what makes `18` §3's <em>"<c>ON_ATTACK</c> counters reset at
///     battle start; <c>ON_KILL</c> counters persist across battles"</em> true structurally rather
///     than by a step somebody remembers.
///   </item>
///   <item>
///     <b>Pre-tick 0a/0b</b> — <see cref="Register"/> every effect that is active at battle start,
///     with <c>activationTick: 0</c>. That is the R8 anchor for perk periodics and for
///     <c>SYS_ENRAGE</c>, which is a <c>BATTLE</c>-scope built-in and therefore anchors at battle
///     start, not at any phase. Then <see cref="Evaluate"/> each with an
///     <c>ON_BATTLE_START</c> occurrence, hero side first and within one actor in
///     <see cref="EffectOrder"/> order.
///   </item>
///   <item>
///     <b>Pre-tick 0c</b> — the boss's phase 1 counts as entered: <see cref="Register"/> its phase-1
///     block at tick 0 and <see cref="Evaluate"/> an <c>ON_PHASE_ENTER</c> occurrence with
///     <c>Phase = 1</c>.
///   </item>
///   <item>
///     <b>Slot 3, every tick</b> — call <see cref="PeriodicDue"/> once per actor in `05` §3.1 actor
///     order, handing it that actor's instance ids. It returns the due ones already in
///     <see cref="EffectOrder"/> order and advances their schedules. This is the <b>only</b>
///     <c>PERIODIC</c> path; <see cref="Evaluate"/> refuses the kind.
///   </item>
///   <item>
///     <b>Slot 4</b> — an <c>ON_ATTACK</c> occurrence per swing, then <c>ON_HIT</c> /
///     <c>ON_CRIT</c> / <c>ON_HIT_TAKEN</c> / <c>ON_DODGE</c> / <c>ON_BLOCK</c> / <c>ON_KILL</c> as
///     `05` §4 resolves them, depth-first, in <see cref="EffectOrder"/> order. Hand in the battle's
///     combat stream — <c>new DeterministicRng(battleSeed, RngStreams.Combat)</c> (`14` §8.1) — so
///     <c>chance</c> can be drawn.
///   </item>
///   <item>
///     <b>Slot 6</b> — <c>ON_DEATH</c> before removal. Nothing here filters on liveness: M2-05's
///     rule is that liveness filters <em>selection</em>, not <em>naming</em>, which is what lets
///     `18` §7.10's Volatile elite fire its own death explosion.
///   </item>
///   <item>
///     <b>Phase transitions</b> — on each entry, <see cref="Register"/> that phase's block at the
///     current tick (its R8 anchor) and <see cref="Deactivate"/> the exited phase's `18` §6
///     <c>PHASE</c>-scoped instances. A burst that crosses two thresholds does this twice in one
///     tick, in order; an instance already live does not re-anchor.
///   </item>
///   <item>
///     <b>Run ops</b> — after a firing, ask <see cref="TriggerRouting"/> what to do with the effect.
///     A combat trigger carrying a `18` §2.5 run/board op is <b>emitted, never resolved</b>.
///   </item>
///   <item>
///     <b>The six run-layer kinds are not yours.</b> <c>ON_TILE_RESOLVED</c>, <c>ON_ROLL</c>,
///     <c>ON_PERK_TAKEN</c>, <c>ON_STAGE_GATE</c>, <c>ON_RUN_START</c> and <c>ON_RUN_END</c> are
///     declared, validated and unit-tested here and fired by M3's run controller. This registry will
///     answer them if asked, because the predicate is the same one; the tick loop simply never asks.
///   </item>
/// </list>
/// <para>
/// ⚠️ <b>What this class does not do, deliberately.</b> It does not order actors, select targets,
/// evaluate `18` §4 conditions, apply ops, or write to the combat log. A trigger says <em>when</em>;
/// `18`'s other four parts say what, to whom, how much and for how long.
/// </para>
/// <para>
/// Not thread-safe, for <c>CombatLog</c>'s reason: one battle, one caller.
/// </para>
/// </remarks>
internal sealed class TriggerRegistry
{
    private readonly Dictionary<EffectInstanceId, TriggerInstance> _instances = new();
    private readonly IRunTriggerCounters _runCounters;

    /// <summary>Builds a registry for one battle.</summary>
    /// <param name="runCounters">
    /// The <b>run's</b> counter store (`18` §3), not a per-battle one. See the type remarks.
    /// </param>
    internal TriggerRegistry(IRunTriggerCounters runCounters)
    {
        ArgumentNullException.ThrowIfNull(runCounters);

        _runCounters = runCounters;
    }

    /// <summary>Every instance registered, in ascending ordinal instance-id order.</summary>
    /// <remarks>Ordered here for <see cref="RunTriggerCounters.Entries"/>' reason.</remarks>
    internal IReadOnlyList<TriggerInstance> Instances =>
        _instances.Values.OrderBy(i => i.Id.Value, EffectInstanceId.Comparer).ToArray();

    /// <summary>
    /// 🔒 Registers and activates one effect instance — and, for a <c>PERIODIC</c>, starts its R8
    /// clock at <paramref name="activationTick"/>.
    /// </summary>
    /// <param name="id">The instance's stable id. See <see cref="EffectInstanceId"/>.</param>
    /// <param name="effect">The authored effect.</param>
    /// <param name="activationTick">The tick the effect becomes active on.</param>
    /// <param name="holderHpFraction">
    /// The holder's HP fraction now, <c>0..1</c>. Required for <c>ON_LOW_HP</c> and ignored
    /// otherwise.
    /// </param>
    /// <exception cref="EffectContextException">
    /// The id is already registered, the effect carries no trigger, the trigger is malformed, or an
    /// <c>ON_LOW_HP</c> was registered without an HP reading.
    /// </exception>
    /// <remarks>
    /// 🔒 <b>A duplicate id is refused, and that refusal is the per-instance rule's teeth.</b>
    /// `18` §3's counters live on the effect <em>instance</em>, so a caller holding two copies of one
    /// effect must name them differently. A registry that quietly merged them would give
    /// <c>PK_FLURRY</c> one shared counter across two copies — a fight that looks entirely legal in
    /// the log and is wrong in the only number that matters.
    /// </remarks>
    internal TriggerInstance Register(
        EffectInstanceId id,
        EffectDefinition effect,
        int activationTick,
        double? holderHpFraction = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentOutOfRangeException.ThrowIfNegative(activationTick);

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

    /// <summary>
    /// Ends an instance — `18` §6's <c>PHASE</c> scope at a phase exit (`05` §3.1), or any other
    /// removal.
    /// </summary>
    /// <param name="id">The instance id.</param>
    internal void Deactivate(EffectInstanceId id) => this[id].Deactivate();

    /// <summary>
    /// 🔒 Whether an instance fires on a moment, and which rule decided (steering S2).
    /// </summary>
    /// <param name="id">The instance id.</param>
    /// <param name="occurrence">The moment.</param>
    /// <param name="rng">The battle's combat draw stream (`14` §8.1), for `18` §3's <c>chance</c>.</param>
    /// <exception cref="EffectContextException">
    /// The id is unregistered, or the occurrence is <c>PERIODIC</c> — see the remarks.
    /// </exception>
    /// <remarks>
    /// 🔒 <b><c>PERIODIC</c> is refused here</b>, the way <c>CombatLog.Append</c> refuses a
    /// <c>Telegraph</c>: it is the one kind whose firing is decided by a schedule rather than by a
    /// moment, and evaluating it through this path would advance that schedule from whatever tick the
    /// caller happened to ask on. <see cref="PeriodicDue"/> is the single path, so the schedule
    /// cannot be advanced twice for one tick or skipped for another.
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

    /// <summary>
    /// 🔒 `05` §3.1 slot 3 — the <c>PERIODIC</c> instances among <paramref name="candidates"/> that
    /// fire on <paramref name="tick"/>, <b>in ascending effect-id order</b>, with their schedules
    /// advanced.
    /// </summary>
    /// <param name="candidates">
    /// The instance ids to consider — one actor's, so the caller can walk actors in `05` §3.1's
    /// order. An id that is not a <c>PERIODIC</c>, or is inactive, is simply not due.
    /// </param>
    /// <param name="tick">The tick slot 3 is running.</param>
    /// <remarks>
    /// <para>
    /// 🔒 <b>The order is <see cref="EffectOrder"/>'s and is imposed here, not assumed.</b> `05`
    /// §3.1 fires slot 3 <em>"in actor order, within one actor in ascending effect-id order"</em>,
    /// and `18` §8 makes that ordinal. A caller cannot change which ward lands first by reordering
    /// the list it hands in. Ordering with a bare <c>OrderBy(x =&gt; x.Id)</c> would consult the
    /// ambient collation and put a German phone and a Linux container in different orders —
    /// <c>StringOrderingRuleTests</c> fails the build on exactly that.
    /// </para>
    /// <para>
    /// 🔒 <b>Calling it twice for one tick is safe and returns nothing the second time</b>, because
    /// a firing advances the instance past the tick. That is the property that makes this the single
    /// path rather than a convenience over <see cref="Evaluate"/>.
    /// </para>
    /// </remarks>
    internal IReadOnlyList<TriggerInstance> PeriodicDue(IEnumerable<EffectInstanceId> candidates, int tick)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfNegative(tick);

        // 🔒 Ordered BEFORE firing, not after. Each firing advances a schedule and a PERIODIC may
        // summon or damage, so the order they are RETURNED in has to be the order they were
        // DECIDED in — sorting a list of already-fired instances would put `05` §3.1's ordering rule
        // on the wrong side of the side effects.
        var ordered = candidates
            .Select(id => this[id])
            .OrderBy(instance => instance.Effect.Id, EffectOrder.IdComparer)
            .ToArray();

        var due = new List<TriggerInstance>(ordered.Length);

        foreach (var instance in ordered)
        {
            if (instance.EvaluatePeriodic(tick) == TriggerOutcome.FIRES)
            {
                due.Add(instance);
            }
        }

        return due;
    }
}
