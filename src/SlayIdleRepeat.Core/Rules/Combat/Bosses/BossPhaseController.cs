using System.Globalization;

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
/// at 70% HP that has not is in phase 1. <see cref="BossPhaseRules.PhaseFor"/> is the stateless
/// half; this is the half that remembers. It is enumerated on
/// <c>StatefulRuleTypeRuleTests.Stateful</c> for exactly that reason.
/// </para>
/// <para>
/// ⚠️ <b>Resolved lazily, never in the constructor.</b> <c>BattleServices.Actors</c> is empty when
/// the seam factory runs — the roster does not exist until the simulation builds it — so the boss is
/// found on the first <see cref="EnterInitialPhase"/> and not before.
/// </para>
/// <para>
/// 🔴 <b>PHASE 1b — every member below except the two readings is a declared, named stub.</b>
/// M2-12's implementation phase owns the bodies; each refusal says what it must do and what the
/// silent alternative would cost.
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
        ArgumentNullException.ThrowIfNull(encounters);

        _services = services;
        _encounters = encounters;
    }

    /// <summary>
    /// The phase a boss is in, or <c>0</c> before pre-tick 0c has entered phase 1.
    /// </summary>
    /// <param name="bossId">The boss's actor id.</param>
    internal int CurrentPhaseOf(string bossId) => _phase.TryGetValue(bossId, out var phase) ? phase : 0;

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

        return _phase.TryGetValue(actor.Id, out var phase) ? phase : null;
    }

    /// <inheritdoc />
    /// <remarks>🔴 <b>PHASE 1b STUB — M2-12's implementation phase owns the body.</b></remarks>
    /// <exception cref="NotSupportedException">Always, until M2-12's implementation phase lands.</exception>
    public void EnterInitialPhase(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        throw new NotSupportedException(
            $"BossPhaseController.EnterInitialPhase('{actor.Id}' at tick " +
            $"{tick.ToString(CultureInfo.InvariantCulture)}) is declared and not written yet — " +
            "M2-12's IMPLEMENTATION phase owns the body. It must refuse a boss it has no " +
            $"BossEncounter for (it holds {_encounters.Count.ToString(CultureInfo.InvariantCulture)}), " +
            "then DEACTIVATE every phase-2 and phase-3 instance in BossEncounter.PhaseOfInstance " +
            "before entering phase 1 — pre-tick 0a registered ALL of them at activation tick 0, so a " +
            "phase-2 8 s PERIODIC left alone ticks from t = 8 s while the boss is still in phase 1 " +
            "(R8). Entering phase 1 is then Enter(): Activate the entering phase's instances, record " +
            "the phase, Append CombatEventType.PhaseChange, and EvaluateAll the ON_PHASE_ENTER " +
            "occurrence. 🔴 Activate ALONE is documented as a no-op on a LIVE instance, so the " +
            "Deactivate is the half that does the work and the half a test must probe through " +
            "AnchorTick (steering S1).");
    }

    /// <inheritdoc />
    /// <remarks>🔴 <b>PHASE 1b STUB — M2-12's implementation phase owns the body.</b></remarks>
    /// <exception cref="NotSupportedException">Always, until M2-12's implementation phase lands.</exception>
    public void AfterHpDecrease(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsBoss)
        {
            // 🔒 Not a stub. `05` §3.1 hands the check EVERY HP decrease of EVERY actor and says
            // "'is this a boss' is the controller's question" — answering nothing for a minion is the
            // implemented behaviour, not a hole, and it is what keeps the refusal below about bosses.
            return;
        }

        throw new NotSupportedException(
            $"BossPhaseController.AfterHpDecrease('{actor.Id}' at tick " +
            $"{tick.ToString(CultureInfo.InvariantCulture)}, hp fraction " +
            $"{actor.HpFraction.ToString("R", CultureInfo.InvariantCulture)}) is declared and not " +
            "written yet — M2-12's IMPLEMENTATION phase owns the body. It is `05` §3.1's loop: " +
            "`while (currentPhase < PhaseFor(hp, firstClear)) Enter(currentPhase + 1)`, so a burst " +
            "from 70% to 20% fires phase 2's entry and THEN phase 3's, both at this tick, in that " +
            "order — and phases never revert, so a heal enters nothing. Each entry Deactivates the " +
            "exiting phase's instances (`18` §6's PHASE-scope end) and Activates the entering " +
            "phase's, which re-anchors their R8 clock here. Reading the phase straight off HP " +
            "instead would silently re-enter phase 1 on every heal (steering S6).");
    }

    /// <inheritdoc />
    /// <remarks>🔴 <b>PHASE 1b STUB — M2-12's implementation phase owns the body.</b></remarks>
    /// <exception cref="NotSupportedException">Always, until M2-12's implementation phase lands.</exception>
    public void AdvanceTick(BattleActor actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);

        if (!actor.IsBoss)
        {
            // 🔒 Not a stub either, and it is the assertion `17` §11's "a new slot perturbs no
            // existing fight" rests on: the loop walks this slot for every actor of every tick, and a
            // fight with no boss must hash identically with and without it.
            return;
        }

        throw new NotSupportedException(
            $"BossPhaseController.AdvanceTick('{actor.Id}' at tick " +
            $"{tick.ToString(CultureInfo.InvariantCulture)}) is declared and not written yet — " +
            "M2-12's IMPLEMENTATION phase owns the body. It is `17` §1's wind-up pass: for every " +
            "ACTIVE PERIODIC instance of the CURRENT phase that carries a " +
            "BossEncounter.LeadSecondsOfInstance entry, emit CombatLog.AppendTelegraph on the tick " +
            "TriggerInstance.NextFiringTick - BossTelegraphs.LeadTicks(lead), once per firing. " +
            "Emitting nothing would leave `17` §11's 'telegraph events emitted 1.0-1.5 s ahead of " +
            "every damaging mechanic' unbuilt with no fight looking wrong (steering S6).");
    }
}
