using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Targeting;
using SlayIdleRepeat.Core.Rules.Effects.Triggers;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// The phase check and pre-tick entry, the boss's three phases — the one controller all bosses run on.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="_phase"/> is the boss's current phase, checked rather than recomputed from HP: phases
/// never revert, so a boss at 70% HP that has been to phase 3 is in phase 3, and one that hasn't is
/// in phase 1 — no function of present HP alone can answer that.
/// <see cref="BossPhaseRules.PhaseFor(double, double, double)"/> is the stateless half; this is the
/// half that remembers, and the only field that accumulates anything.
/// </para>
/// <para>
/// Resolved lazily, never in the constructor: the roster does not exist until the simulation builds
/// it, so the boss is found on the first <see cref="EnterInitialPhase"/> and not before.
/// </para>
/// <para>
/// One phase entry is four steps: deactivate every instance of the phase being left, activate every
/// instance of the phase being entered (at the entry tick, so a periodic anchors from phase entry),
/// record the change and log it, then sweep <c>ON_PHASE_ENTER</c> for that phase in ascending
/// effect-id order. <c>SYS_ENRAGE</c> is reached by none of it, because it is not in
/// <see cref="BossEncounter.PhaseOfInstance"/> at all — a transition walks the map, and what is not
/// in the map cannot be re-anchored by one.
/// </para>
/// </remarks>
internal sealed class BossPhaseController : IBossPhases
{
    private readonly BattleServices _services;
    private readonly IReadOnlyList<BossEncounter> _encounters;

    /// <summary>The boss's current phase, by actor id. See the class remarks.</summary>
    private readonly Dictionary<string, int> _phase = new(StringComparer.Ordinal);

    /// <summary>Builds the phase controller for one fight.</summary>
    /// <param name="services">The battle — its roster, its clock, its log and its trigger registry.</param>
    /// <param name="encounters">The bosses it knows, one <see cref="BossEncounter"/> each.</param>
    internal BossPhaseController(BattleServices services, params BossEncounter[] encounters)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(encounters);

        _services = services;
        _encounters = encounters;
    }

    /// <summary>
    /// The phase a boss is in, or <c>null</c> before pre-tick 0c has entered phase 1.
    /// </summary>
    /// <param name="bossId">The boss's actor id.</param>
    internal int? CurrentPhaseOf(string bossId) =>
        _phase.TryGetValue(bossId, out var phase) ? phase : null;

    /// <inheritdoc />
    /// <remarks>
    /// A reading of the accumulator above and nothing more — no HP, no thresholds, no transition. An
    /// answer that recomputed the phase from HP would report a boss healed above 66% as back in
    /// phase 1 and end a phase-3 aura that is still running.
    /// </remarks>
    public int? CurrentPhase(BattleActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);

        return CurrentPhaseOf(actor.Id);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The de-anchoring below is the half of this method that does the work: pre-tick registration
    /// activates every plan effect at tick 0, phase-2/3 blocks included (they have to be on the plan
    /// for the effect table to name them), so a phase-2 periodic left alone would tick from battle
    /// start while the boss is still in phase 1.
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
    /// A burst from 70% to 20% fires phase 2's entry and then phase 3's, both inside this one call;
    /// a heal enters nothing because the loop only ever walks upward.
    /// </remarks>
    public void AfterHpDecrease(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsBoss)
        {
            return;
        }

        if (EncounterOf(actor.Id) is not { } encounter)
        {
            // A boss with no encounter never gets this far: EnterInitialPhase refuses it first.
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
    /// The wind-up pass: for every active <c>PERIODIC</c> instance of the boss's current phase that
    /// carries an authored lead, one <c>Telegraph</c> on the tick <c>NextFiringTick - leadTicks</c>.
    /// "Once per firing" is a property of the arithmetic, not a remembered set: between two firings
    /// <c>NextFiringTick</c> does not move, so the condition is true on exactly one tick.
    /// </remarks>
    public void AdvanceTick(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsBoss)
        {
            return;
        }

        // Already bucketed by phase and ordered at encounter-build time — see
        // BossEncounter.AnnouncingOfPhase.
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

        // A wind-up announces a landing, so a landing the fight cannot reach is not announced: a
        // Telegraph for a firing beyond MaxTicks would have no hit after it in the log.
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
    /// Who the announced mechanic will hit — the effect's own target resolution, so the wind-up
    /// points at the actor the firing will.
    /// </summary>
    /// <remarks>
    /// <c>CombatActor.None</c> where the token resolves to nobody or to more than one actor: a
    /// <c>CombatEvent</c> carries a single target slot, and naming the first of several would be a
    /// wind-up over the wrong head.
    /// </remarks>
    private byte AnnouncedTargetOf(BattleActor boss, EffectDefinition effect)
    {
        var resolved = TargetResolver.Resolve(
            EffectDefaults.TargetOf(effect), _services.ContextFor(boss));

        return resolved.Count == 1 && resolved[0] is BattleActor target
            ? target.LogId
            : CombatActor.None;
    }

    /// <summary>One phase entry — the four steps the class remarks enumerate.</summary>
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
    /// Ends the <c>PHASE</c> scope, for an instance the registry actually knows.
    /// </summary>
    /// <remarks>
    /// An instance in the phase map that is not registered is not a fault: an outcome row is an
    /// ordinary phase mechanic carrying no trigger of its own, and only triggered effects register.
    /// </remarks>
    private void Deactivate(EffectInstanceId instance)
    {
        if (_services.Triggers.IsRegistered(instance))
        {
            _services.Triggers.Deactivate(instance);
        }
    }

    /// <summary>Anchors the entry tick, for an instance the registry knows.</summary>
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
    /// A boss on the roster this controller has no <see cref="BossEncounter"/> for is a wiring gap.
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
