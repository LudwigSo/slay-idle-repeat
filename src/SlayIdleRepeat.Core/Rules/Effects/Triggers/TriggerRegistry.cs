using System.Globalization;
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
///     <c>chance</c> can be drawn. Use <see cref="EvaluateAll"/> for a flat sweep; drive a cascade
///     that changes the roster one instance at a time through <see cref="Evaluate"/>.
///   </item>
///   <item>
///     <b>Slot 5 fires nothing of its own.</b> `18` §7.7's pet actives carry no trigger — the
///     ability's cooldown is the wrapper's — so a pet ability raises only the <c>ON_HIT</c> family
///     of slot 4, through the damage it resolves (`05` §4.2). Said because a tick-loop author
///     otherwise has to infer it from the absence of a slot-5 item.
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
///     <b>Run ops</b> — after a firing, ask <see cref="TriggerRouting"/> what to do with the effect,
///     passing <see cref="TriggerLayer.COMBAT"/> as the firing layer. A combat trigger carrying a
///     `18` §2.5 run/board op is <b>emitted, never resolved</b>.
///   </item>
///   <item>
///     <b>The six run-layer kinds are not yours.</b> <c>ON_TILE_RESOLVED</c>, <c>ON_ROLL</c>,
///     <c>ON_PERK_TAKEN</c>, <c>ON_STAGE_GATE</c>, <c>ON_RUN_START</c> and <c>ON_RUN_END</c> are
///     declared, validated and unit-tested here and fired by M3's run controller. This registry will
///     answer them if asked, because the predicate is the same one; the tick loop simply never asks.
///   </item>
/// </list>
///
/// <para>
/// ═══ <b>THE SIX COMBAT KINDS `05` §3.1 DOES NOT PUT IN A NUMBERED SLOT</b> ═══
/// </para>
/// <para>
/// The tick order names six of its eight slots by trigger. These are the rest, stated so nobody has
/// to guess:
/// </para>
/// <list type="bullet">
///   <item><b><c>ALWAYS</c></b> — no moment at all. `18` §1.1 re-evaluates it <em>"at every
///   resolution pass"</em>, so it belongs to M2-02's resolver rather than to a slot. This registry
///   answers <c>FIRES</c> for a live one if asked, which is what a passive means.</item>
///   <item><b><c>ON_BATTLE_END</c></b> — after slot 8's break, at the fight's last tick, with
///   <see cref="TriggerOccurrence.HeroWon"/> set from the outcome. It is the only kind that reads
///   that field. `18` §2.5 fixes its position relative to the run queue: the queue is applied
///   <em>"after the outcome is fixed, before <c>ON_BATTLE_END</c> effects are granted"</em>.</item>
///   <item><b><c>ON_LOW_HP</c></b> — after <b>every</b> HP change of the holder, alongside `05`
///   §3.1's phase check, DoT ticks in slot 1 included. It is a <em>crossing</em>, so a skipped
///   observation is a firing lost: hand in the post-change fraction and let the instance hold the
///   previous one.</item>
///   <item><b><c>ON_LETHAL</c></b> — inside `05` §4, when the hit would be fatal, before slot 6.
///   ⚠️ `05` §3.1's anti-loop rule — <c>SURVIVE_LETHAL</c> and <c>REVIVE</c> fire <em>"at most their
///   authored <c>once</c> count per battle"</em> — is a rule about the <b>op</b>, so it is M2-08's
///   to apply; <see cref="TriggerInstance.FireCount"/> is what it reads.</item>
///   <item><b><c>ON_HEAL</c></b> — inside `05` §4.3's <c>Heal()</c>, for every heal: lifesteal,
///   HoT cadence in slot 1, and a <c>HEAL</c> op. §4.3 fires it after the HP is applied, which is
///   what makes <c>HEAL_AMOUNT</c> and <c>OVERHEAL_AMOUNT</c> readable.</item>
///   <item><b><c>ON_REVIVE</c></b> — when the <c>REVIVE</c> op or the ad revive returns the actor
///   from 0 HP. `18` §3 is explicit that <c>SURVIVE_LETHAL</c> does <b>not</b> count, because the
///   actor never died.</item>
/// </list>
///
/// <para>
/// ═══ <b>TWO THINGS THIS REGISTRY DELIBERATELY DOES NOT KNOW</b> ═══
/// </para>
/// <para>
/// <b>1. It is actor-blind.</b> A <see cref="TriggerInstance"/> carries no actor, and
/// <see cref="PeriodicDue"/> and <see cref="EvaluateAll"/> take the ids the caller wants asked. The
/// actor → instances map is the tick loop's, because `05` §3.1's actor order — <em>"hero, pets in
/// slot order, enemies by index"</em>, with summons appended — is the loop's to maintain as actors
/// spawn and die, and a second copy of it here would be a second thing to keep in step.
/// </para>
/// <para>
/// <b>2. It does not mint <see cref="EffectInstanceId"/>s</b>, and the minting rule is the one thing
/// M2-08 must get right that nothing here can check. Two obligations, from `18` §3: every live copy
/// of an effect needs a <b>distinct</b> id (<see cref="Register"/> refuses a duplicate, which is the
/// rule's teeth), and an <c>ON_KILL</c> id must be <b>stable across battles</b> because its counter
/// is the run's. So a hero-side holding — a perk in a draft slot, an affix on a gear item — takes an
/// id the run layer owns and repeats every fight; a boss, elite or summon effect has no run-scoped
/// holding and takes a battle-local id, which is sound because no `17` mechanic carries
/// <c>ON_KILL</c>.
/// </para>
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

    private int _lastPeriodicTick;

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

        // 🔒 Checked HERE and not left to EffectInstanceId.Of. A record struct's generated
        // constructor is public and `default(EffectInstanceId)` carries a null value, so `Of`'s
        // guard is a convenience rather than a guarantee — and `default` is a perfectly good
        // dictionary key, so an unnamed instance would register cleanly and every other unnamed
        // instance would share its counter. The per-instance rule fails silently in the direction
        // that looks like it works.
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

    /// <summary>
    /// Ends an instance — `18` §6's <c>PHASE</c> scope at a phase exit (`05` §3.1), or any other
    /// removal.
    /// </summary>
    /// <param name="id">The instance id.</param>
    internal void Deactivate(EffectInstanceId id) => this[id].Deactivate();

    /// <summary>
    /// Re-activates an instance that was deactivated — a re-grant. Its R8 clock and its
    /// <c>ON_LOW_HP</c> arming both restart from <paramref name="tick"/>; a live instance is
    /// untouched.
    /// </summary>
    /// <param name="id">The instance id.</param>
    /// <param name="tick">The tick the effect becomes active again on.</param>
    /// <param name="holderHpFraction">
    /// The holder's HP fraction now, <c>0..1</c>. Required for <c>ON_LOW_HP</c> and ignored
    /// otherwise — a re-grant is a new arming for the same reason it is a new clock.
    /// </param>
    /// <remarks>
    /// Distinct from <see cref="Register"/>, which refuses a duplicate id. Boss phases never revert
    /// (`05` §3.1), so this is not on the phase path — it is for the cases that do repeat: a summon
    /// re-entering, an ad revive, and M2-06's re-grant of an expired effect.
    /// </remarks>
    internal void Activate(EffectInstanceId id, int tick, double? holderHpFraction = null) =>
        this[id].Activate(tick, holderHpFraction);

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
    /// 🔒 The same question over several instances, <b>in ascending effect-id order</b> — what
    /// `05` §3.1 asks in the pre-tick's <c>ON_BATTLE_START</c> sweep, in slot 4's on-hit cascade and
    /// in slot 6's death resolution.
    /// </summary>
    /// <param name="candidates">
    /// The instance ids to ask — one actor's, so the caller can walk actors in `05` §3.1's order.
    /// </param>
    /// <param name="occurrence">The moment.</param>
    /// <param name="rng">The battle's combat draw stream (`14` §8.1).</param>
    /// <returns>The instances that fired, in the order they were decided.</returns>
    /// <remarks>
    /// <para>
    /// 🔒 <b>It exists so that "ascending effect-id order" is not something four call sites have to
    /// remember.</b> `05` §3.1 keys on that order in five places and `18` §8 makes it ordinal;
    /// <see cref="PeriodicDue"/> already imposes it for slot 3, and leaving the other four to the
    /// caller would re-open at the call site exactly the defect <c>StringOrderingRuleTests</c>
    /// exists to close — a bare <c>OrderBy(e =&gt; e.Id)</c> that is right on the machine it was
    /// written on.
    /// </para>
    /// <para>
    /// ⚠️ <b>Not for a cascade that changes the roster.</b> `05` §3.1 resolves on-hit triggers
    /// <em>"immediately, depth-first"</em>: if firing one instance can register another, or kill the
    /// holder, the tick loop must drive them one at a time through <see cref="Evaluate"/> and order
    /// them itself with <see cref="EffectOrder.IdComparer"/>. This is the flat case — every candidate
    /// decided against one unchanging moment.
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
    /// <para>
    /// 🔒 <b>Ticks never go backwards</b>, for <c>CombatLog.Append</c>'s reason and one more: a tick
    /// below a previous call answers <c>NOT_DUE</c> for everything, so a caller that walked its loop
    /// wrongly would lose every firing in between with nothing going red. Every other wiring gap in
    /// this class throws; so does this one.
    /// </para>
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

    /// <summary>
    /// 🔒 The candidates as instances, in `05` §3.1's <em>"ascending effect-id order"</em> — resolved
    /// and ordered <b>before</b> any of them fires.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Materialised first, so that an unregistered id throws before any instance has been advanced
    /// and the registry cannot be left half-fired. Ordering after the firings would put `05` §3.1's
    /// rule on the wrong side of their side effects — a <c>PERIODIC</c> may summon or damage.
    /// </para>
    /// <para>
    /// 🔒 <b>The tie-break is the instance id, and it is load-bearing.</b> The key is the
    /// <em>effect</em> id, while <see cref="EffectInstanceId"/> exists precisely because one actor can
    /// hold two copies of one effect (`18` §3). Two copies of <c>PK_AEGIS</c> tie on the effect id,
    /// and a stable sort would then let the caller's list order decide which ward lands first —
    /// falsifying the claim above and making the fight depend on how the tick loop happened to build
    /// a list.
    /// </para>
    /// <para>
    /// 🔒 <b>A duplicate id in one call is refused.</b> It is normally harmless, but a schedule that
    /// is behind (<c>due = 40</c>, <c>interval = 20</c>, <c>tick = 100</c>) leaves the instance due
    /// again immediately, so the duplicate fires twice inside one tick and appears twice in the
    /// result — a boss summoning two waves on one tick, from a caller bug no log would explain.
    /// </para>
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

    /// <summary>`18` §8's ordinal effect-id order, tie-broken by the instance id.</summary>
    private static int ByEffectThenInstanceId(TriggerInstance left, TriggerInstance right)
    {
        var byEffect = EffectOrder.IdComparer.Compare(left.Effect.Id, right.Effect.Id);

        return byEffect != 0
            ? byEffect
            : EffectInstanceId.Comparer.Compare(left.Id.Value, right.Id.Value);
    }
}
