using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Targeting;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `05` §3.1's phase check and pre-tick 0c, `17` §1's three phases — the one controller all eight
/// bosses run on.
/// </summary>
/// <remarks>
/// <para>
/// ═══ 🔒 <b>THE ONE PIECE OF STATE, AND WHY A STATELESS FUNCTION CANNOT REPLACE IT</b> ═══
/// </para>
/// <para>
/// <see cref="_phase"/> is the boss's <em>current</em> phase, and `05` §3.1 states the check over it
/// rather than over HP: <em>"while <c>currentPhase &lt; PhaseFor(hp)</c> … enter the next phase"</em>,
/// and <em>"phases never revert — healing back above a threshold does not re-enter an earlier
/// phase."</em> Both sentences are about what already happened, so no function of the roster's
/// present HP can answer them: a boss at 70% HP that has been to phase 3 is in phase 3, and a boss
/// at 70% HP that has not is in phase 1. <see cref="BossPhaseRules.PhaseFor(double, double, double)"/>
/// is the stateless half; this is the half that remembers. It is enumerated on
/// <c>StatefulRuleTypeRuleTests.Stateful</c> for exactly that reason, and it is the <b>only</b> field
/// that accumulates anything — the wind-up pass below deliberately remembers nothing.
/// </para>
/// <para>
/// ⚠️ <b>Resolved lazily, never in the constructor.</b> <c>BattleServices.Actors</c> is empty when
/// the seam factory runs — the roster does not exist until the simulation builds it — so the boss is
/// found on the first <see cref="EnterInitialPhase"/> and not before.
/// </para>
///
/// <para>
/// ═══ 🔒 <b>ONE ENTRY, IN FOUR STEPS, AND EVERY ONE OF THEM MATTERS</b> ═══
/// </para>
/// <list type="number">
///   <item><b>End the phase being left</b> — <c>Deactivate</c> every instance of phase <c>p−1</c>,
///   which is `18` §6's <c>PHASE</c> scope <em>"ending when the boss exits the phase in which the
///   effect was applied"</em>.</item>
///   <item><b>Start the phase being entered</b> — <c>Activate</c> every instance of phase <c>p</c> at
///   the entry tick, which is R8's anchor and therefore `17` §1.1's <em>"fires every N seconds from
///   phase entry"</em>. 🔴 <c>Activate</c> is documented as leaving a <b>live</b> instance untouched,
///   so it is step 1 — and pre-tick 0c's blanket de-anchoring — that does the work here.</item>
///   <item><b>Record it</b>, and append <c>CombatEventType.PhaseChange</c> at the moment of the
///   change (`05` §3.1 step 7).</item>
///   <item><b>Sweep <c>ON_PHASE_ENTER</c></b> for that phase, in ascending effect-id order.</item>
/// </list>
/// <para>
/// 🔒 <b><c>SYS_ENRAGE</c> is reached by none of it</b>, because it is not in
/// <see cref="BossEncounter.PhaseOfInstance"/> at all. That is structural rather than remembered: a
/// transition walks the map, and what is not in the map cannot be re-anchored by one.
/// </para>
/// </remarks>
internal sealed class BossPhaseController : IBossPhases
{
    private readonly BattleServices _services;
    private readonly IReadOnlyList<BossEncounter> _encounters;

    /// <summary>
    /// 🔒 The boss's current phase, by actor id — the accumulator `05` §3.1's <em>"phases never
    /// revert"</em> is stated over. See the class remarks.
    /// </summary>
    private readonly Dictionary<string, int> _phase = new(StringComparer.Ordinal);

    /// <summary>Builds the phase controller for one fight.</summary>
    /// <param name="services">The battle — its roster, its clock, its log and its trigger registry.</param>
    /// <param name="encounters">The bosses it knows, one <see cref="BossEncounter"/> each.</param>
    internal BossPhaseController(BattleServices services, params BossEncounter[] encounters)
    {
        // Both, at the boundary: a null `services` would otherwise surface as a NullReferenceException
        // at pre-tick 0c — the first tick of a fight, and several frames away from the wiring that
        // caused it. BossOutcomes guards the same argument the same way.
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(encounters);

        _services = services;
        _encounters = encounters;
    }

    /// <summary>
    /// The phase a boss is in, or <c>null</c> before pre-tick 0c has entered phase 1.
    /// </summary>
    /// <param name="bossId">The boss's actor id.</param>
    /// <remarks>
    /// 🔒 <b>One spelling of "no phase yet", and it is <c>null</c>.</b> This reading and
    /// <see cref="CurrentPhase"/> answered <c>0</c> and <c>null</c> respectively while they were two
    /// methods; they are one lookup now, because two spellings of an absent phase is two things a
    /// caller has to test for and one of them will eventually be forgotten.
    /// </remarks>
    internal int? CurrentPhaseOf(string bossId) =>
        _phase.TryGetValue(bossId, out var phase) ? phase : null;

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 A <b>reading</b> of the accumulator above and nothing more — no HP, no thresholds, no
    /// transition. `18` §6's <c>PHASE</c> scope asks this question on every tick of slot 2, and an
    /// answer that recomputed the phase from HP would report a boss healed above 66% as back in
    /// phase 1 and end a phase-3 aura that `05` §3.1 says is still running.
    /// </remarks>
    public int? CurrentPhase(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return CurrentPhaseOf(actor.Id);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🔴 <b>The de-anchoring, and it is the half of this method that does the work.</b> Pre-tick 0a
    /// registers <b>every</b> plan effect at activation tick 0 — phase-2 and phase-3 blocks
    /// included, because they have to be on the plan for the battle's effect table to name them — so
    /// a phase-2 8 s <c>PERIODIC</c> left alone would tick from <c>t = 8 s</c> while the boss is
    /// still in phase 1, which is an R8 violation in a fight nobody authored.
    /// </remarks>
    public void EnterInitialPhase(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var encounter = EncounterOf(actor.Id) ?? throw Unscripted(actor);

        foreach (var (instance, phase) in encounter.PhaseOfInstance)
        {
            if (phase > BossPhaseRules.FirstPhase)
            {
                Deactivate(instance);
            }
        }

        Enter(actor, encounter, BossPhaseRules.FirstPhase, tick);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 🔒 `05` §3.1's loop, <em>"while <c>currentPhase &lt; PhaseFor(hp)</c>, enter the next phase in
    /// order"</em> — so a burst from 70% to 20% fires phase 2's entry and <b>then</b> phase 3's, both
    /// inside this one call, and a heal enters nothing because the loop only ever walks upward.
    /// </remarks>
    public void AfterHpDecrease(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsBoss)
        {
            // 🔒 Not a hole. `05` §3.1 hands the check EVERY HP decrease of EVERY actor and says
            // "'is this a boss' is the controller's question" — answering nothing for a minion is
            // the implemented behaviour.
            return;
        }

        if (EncounterOf(actor.Id) is not { } encounter)
        {
            // 🔒 A boss with no encounter never gets this far: EnterInitialPhase refuses it at
            // pre-tick 0c, which is the one place a wiring gap can be reported before a fight runs.
            return;
        }

        var reading = BossPhaseRules.PhaseFor(
            actor.HpFraction, encounter.Phase2HpFraction, encounter.Phase3HpFraction);

        while (CurrentPhaseOf(actor.Id) is { } current && current < reading)
        {
            Enter(actor, encounter, current + 1, tick);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// 🔒 `17` §1's wind-up pass: for every <b>active</b> <c>PERIODIC</c> instance of the boss's
    /// <b>current</b> phase that carries an authored lead, one <c>Telegraph</c> on the tick
    /// <c>NextFiringTick − leadTicks</c>.
    /// </para>
    /// <para>
    /// 🔒 <b>"Once per firing" is a property of the arithmetic, not of a remembered set.</b> Between
    /// two firings <c>NextFiringTick</c> does not move, so <c>tick == next − lead</c> is true on
    /// exactly one tick; slot 3 then advances the schedule and the next window opens once. That is
    /// why this controller accumulates nothing but the phase.
    /// </para>
    /// </remarks>
    public void AdvanceTick(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsBoss)
        {
            // 🔒 The assertion `17` §11's "a new slot perturbs no existing fight" rests on: the loop
            // walks this slot for every actor of every tick, and a fight with no boss must hash
            // identically with and without it.
            return;
        }

        // 🔒 Already bucketed by phase and already ordered, at encounter-build time — see
        // BossEncounter.AnnouncingOfPhase. Selecting and sorting here instead would allocate a
        // filter, a closure and a sort buffer on every one of `05` §3's up-to-1800 ticks, for a list
        // that cannot change during a fight; a phase with no wind-up costs one failed probe.
        if (EncounterOf(actor.Id) is not { } encounter ||
            CurrentPhaseOf(actor.Id) is not { } phase ||
            !encounter.AnnouncingOfPhase.TryGetValue(phase, out var announcing))
        {
            return;
        }

        for (var i = 0; i < announcing.Count; i++)
        {
            Announce(actor, encounter, announcing[i], tick);
        }
    }

    /// <summary>
    /// Emits one instance's wind-up if this tick is the one <c>NextFiringTick − leadTicks</c> names.
    /// </summary>
    private void Announce(
        BattleActor boss, BossEncounter encounter, EffectInstanceId instance, int tick)
    {
        if (!_services.Triggers.IsRegistered(instance))
        {
            return;
        }

        var registered = _services.Triggers[instance];

        if (!registered.IsActive || registered.NextFiringTick is not { } firing)
        {
            return;
        }

        var lead = encounter.LeadSecondsOfInstance[instance];

        if (tick != firing - BossTelegraphs.LeadTicks(lead))
        {
            return;
        }

        // 🔒 T4 (BossTelegraphs) — a wind-up announces a landing, so a landing the fight cannot reach
        // is not announced. `05` §3 bounds the fight at CombatRules.MaxTicks; a Telegraph emitted for
        // a firing beyond it would be a wind-up with no hit after it in the log, which the replayer
        // would draw and nothing would ever resolve. It is the one wind-up rule that is NOT an
        // authoring rule: which tick a firing lands on depends on the HP-driven tick the phase was
        // entered at, so BossEncounterBuilder cannot decide it and this pass has to.
        if (firing >= _services.Rules.MaxTicks)
        {
            return;
        }

        _services.Log.AppendTelegraph(
            tick,
            boss.LogId,
            AnnouncedTargetOf(boss, registered.Effect),
            _services.EffectIndexOf(registered.Effect),
            lead);
    }

    /// <summary>
    /// 🔒 Who the announced mechanic will hit — `18` §5's own resolution of the effect's
    /// <c>target</c>, so the wind-up points at the actor the firing will.
    /// </summary>
    /// <remarks>
    /// <c>CombatActor.None</c> where the token resolves to nobody (every candidate dead) or to more
    /// than one actor: `05` §7's <c>CombatEvent</c> carries a single target slot, and naming the
    /// first of several would be a wind-up over the wrong head.
    /// </remarks>
    private byte AnnouncedTargetOf(BattleActor boss, EffectDefinition effect)
    {
        var resolved = TargetResolver.Resolve(
            EffectDefaults.TargetOf(effect), _services.ContextFor(boss));

        return resolved.Count == 1 && resolved[0] is BattleActor target
            ? target.LogId
            : CombatActor.None;
    }

    /// <summary>🔒 One phase entry — the four steps the class remarks enumerate.</summary>
    private void Enter(BattleActor boss, BossEncounter encounter, int phase, int tick)
    {
        foreach (var (instance, owned) in encounter.PhaseOfInstance)
        {
            if (owned == phase - 1)
            {
                Deactivate(instance);
            }
            else if (owned == phase)
            {
                Activate(instance, tick, boss.HpFraction);
            }
        }

        _phase[boss.Id] = phase;

        _services.Log.Append(
            tick, CombatEventType.PhaseChange, boss.LogId, boss.LogId, phase);

        _services.FirePhaseEntry(boss, phase);
    }

    /// <summary>
    /// `18` §6's <c>PHASE</c>-scope end, for an instance the registry actually knows.
    /// </summary>
    /// <remarks>
    /// ⚠️ An instance in the phase map that is <b>not</b> registered is not a fault: an outcome row
    /// is an ordinary phase mechanic carrying no trigger of its own, and
    /// <c>BattleSimulation.RegisterHoldings</c> registers only triggered effects.
    /// </remarks>
    private void Deactivate(EffectInstanceId instance)
    {
        if (_services.Triggers.IsRegistered(instance))
        {
            _services.Triggers.Deactivate(instance);
        }
    }

    /// <summary>R8's anchor — the entry tick, for an instance the registry knows.</summary>
    private void Activate(EffectInstanceId instance, int tick, double holderHpFraction)
    {
        if (_services.Triggers.IsRegistered(instance))
        {
            _services.Triggers.Activate(instance, tick, holderHpFraction);
        }
    }

    private BossEncounter? EncounterOf(string bossId)
    {
        foreach (var encounter in _encounters)
        {
            if (string.Equals(encounter.BossId, bossId, StringComparison.Ordinal))
            {
                return encounter;
            }
        }

        return null;
    }

    /// <summary>
    /// 🔒 A boss on the roster this controller has no <see cref="BossEncounter"/> for is a wiring
    /// gap, refused at pre-tick 0c — <c>NoBossPhases</c>' shape, for its reason.
    /// </summary>
    private EffectContextException Unscripted(BattleActor actor) =>
        new(
            actor.Id,
            "it is a boss and this controller holds no encounter for it — it knows " +
            (_encounters.Count == 0
                ? "no boss at all"
                : string.Join(", ", _encounters.Select(e => $"'{e.BossId}'"))),
            "`05` §3.1's pre-tick 0c fires the boss's ON_PHASE_ENTER(1) effects and the phase check " +
            "runs after every HP decrease thereafter. Running a boss without its encounter is a " +
            "fight with its mechanics silently deleted, which the balance harness would read as the " +
            "boss being weak. Build one through BossEncounterBuilder.Build and hand it in.");
}
