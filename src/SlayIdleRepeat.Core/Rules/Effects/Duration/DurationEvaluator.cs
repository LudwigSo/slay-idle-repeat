using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Duration;

/// <summary>
/// 🔒 `18` §6 — whether an applied effect has ended, and which boundary ended it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>A pure function of one application and one probe.</b> It holds no timers, no registry and no
/// clock: `05` §3.1's tick loop (M2-08) and its cadence engine (M2-10) both drive it, and neither
/// should have to agree with the other about how an effect's remaining time is stored. Battle time
/// arrives as a number on the probe, because `30` §3 keeps every clock out of <c>Core</c>.
/// </para>
/// <para>
/// 🔒 <b>Order of the checks, and why it is the order `18` §6 writes.</b> <c>INSTANT</c> first,
/// because it never persists to be timed at all. Then the <c>until</c> terminator and the timer —
/// <em>"<c>until</c> fields fire whichever comes first, terminator or timer"</em>, and a probe is a
/// snapshot, so when both are true on the same probe the terminator is reported: it is the
/// instantaneous event (`05` §4.1's <c>WardBroken</c> fires at a moment), while the timer is a
/// threshold that stays crossed. Then the scope, which is what ends an effect that neither fired for.
/// </para>
/// </remarks>
internal static class DurationEvaluator
{
    private static readonly DurationOutcome Running = new(false, DurationEndReason.NotEnded);

    /// <summary>Evaluates one applied effect against one probe of the battle.</summary>
    /// <param name="application">The effect instance and where it was applied.</param>
    /// <param name="probe">The battle as it stands at the moment of the question.</param>
    /// <exception cref="EffectContextException">
    /// The probe contradicts the application — a boss phase that went backwards, or a phase-scoped
    /// effect applied inside a phase whose probe carries none. See <see cref="Phase"/>.
    /// </exception>
    internal static DurationOutcome Evaluate(EffectApplication application, DurationProbe probe)
    {
        // 🔒 `18` §1 writes "duration": null on the canonical passive — a STAT_ADD_PCT ALWAYS perk
        // that lasts as long as its source is equipped. That is not a timed instance at all, and the
        // battle evaluator has no boundary to end it at. Reading the absence as INSTANT would delete
        // every passive in the game on the tick it was applied.
        if (application.Duration is not { } duration)
        {
            return Running;
        }

        if (duration.Scope == DurationScope.INSTANT)
        {
            return new DurationOutcome(true, DurationEndReason.Instant);
        }

        if (duration.Until is { } terminator && Fires(terminator, probe, application))
        {
            return new DurationOutcome(true, Reason(terminator, application));
        }

        if (duration.Seconds is { } seconds &&
            probe.BattleTimeSeconds - application.AppliedAtSeconds >= seconds)
        {
            return new DurationOutcome(true, DurationEndReason.TimerElapsed);
        }

        return duration.Scope switch
        {
            DurationScope.BATTLE => Battle(probe),
            DurationScope.PHASE => Phase(application, probe),

            // 🔒 The run layer's three. They outlive a battle by definition, so the battle-scoped
            // evaluator never ends one — and does not pretend to. The controller that does is M3's,
            // over M1-05's Run aggregate (kickoff A4, `18` §2.5). A placeholder here would be a
            // second mechanism to find and delete later; a declared scope with no consumer is the
            // correct end state, and DurationScopes.OutlivesTheBattle is what M3 keys on.
            DurationScope.STAGE or DurationScope.RUN or DurationScope.PERMANENT => Running,

            _ => throw new EffectContextException(
                duration.Scope.ToString(),
                $"'{application.EffectId}' names a scope that is not one of 18 §6's six",
                "18 §11: '6 duration scopes = 5 + PHASE' — INSTANT, BATTLE, PHASE, STAGE, RUN, PERMANENT."),
        };
    }

    private static DurationOutcome Battle(DurationProbe probe) =>
        probe.BattleEnded ? new DurationOutcome(true, DurationEndReason.BattleEnded) : Running;

    /// <summary>
    /// 🔒 `18` §6: <c>PHASE</c> <em>"ends when the boss exits the phase in which the effect was
    /// applied"</em>, and <em>"outside a boss fight it behaves as <c>BATTLE</c>"</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔴 <b>R3 — every boss <c>AURA</c> mechanic is <c>PHASE</c>-scoped.</b> §6 says so by
    /// definition and §7.10 authors Gulgrot's Bog Air that way; `18` §7.8's Thornmaw phase-3 RAGE is
    /// authored as <c>{"seconds": 999, "scope": "BATTLE"}</c> and is a <b>pre-<c>PHASE</c>
    /// artifact</b> — `18` §11 records <c>PHASE</c> arriving with the A7 batch. The two are equivalent
    /// for Thornmaw <em>only</em> because phase 3 is never exited, which is why it survived review.
    /// Erratum on §7.8; M2-13 authors the data, and this method is what makes the idiom unnecessary.
    /// </para>
    /// <para>
    /// 🔒 <b><see cref="EffectApplication.AppliedInPhase"/> is the sole discriminator of "inside a
    /// boss fight".</b> Not the probe's phase, and not a separate flag: §6 states the fallback in
    /// terms of where the effect was <em>applied</em>, and two sources of truth for one fact would let
    /// a caller that set one and not the other silently pick the other branch.
    /// </para>
    /// </remarks>
    private static DurationOutcome Phase(EffectApplication application, DurationProbe probe)
    {
        if (application.AppliedInPhase is not { } appliedIn)
        {
            return Battle(probe);
        }

        if (probe.CurrentPhase is not { } current)
        {
            throw new EffectContextException(
                nameof(DurationScope.PHASE),
                $"'{application.EffectId}' was applied in phase {Number(appliedIn)} and the probe carries no phase at all",
                "18 §6 ends a PHASE effect when the boss exits the phase it was applied in, so there " +
                "is a boss and it is in some phase. A fight cannot stop being a boss fight, and " +
                "reading the absence as 'outside a boss fight' would silently convert the effect to " +
                "BATTLE scope mid-fight — the one reading 18 §6 reserves for an effect that was " +
                "never applied inside a phase.");
        }

        if (current < appliedIn)
        {
            throw new EffectContextException(
                nameof(DurationScope.PHASE),
                $"'{application.EffectId}' was applied in phase {Number(appliedIn)} and the probe reports phase {Number(current)}",
                "05 §3.1: 'Phases never revert — healing back above a threshold does not re-enter an " +
                "earlier phase.' A backwards phase is a defect in whatever produced the probe, and " +
                "reading it as 'still inside the applied phase' would keep a boss AURA alive across " +
                "an exit 18 §6 says ends it.");
        }

        return current > appliedIn
            ? new DurationOutcome(true, DurationEndReason.PhaseExited)
            : Running;
    }

    /// <summary>
    /// 🔒 `05` §4.1 — <c>WardBroken</c> fires <em>"the moment the pool reaches 0 <b>through
    /// damage</b>"</em>. <em>"Segment expiry silently removes its remainder
    /// (<c>StatusExpired</c>), and does <b>not</b> fire <c>WardBroken</c>."</em>
    /// </summary>
    /// <remarks>
    /// 🔒 <b>This is why <see cref="DurationProbe.OwnerWardEvent"/> is a
    /// <see cref="WardPoolEvent"/> and not a <c>bool</c>.</b> A boolean "the ward broke" would put
    /// the distinction in the caller's discipline, where it is a comment; as an enum the caller
    /// cannot say the pool emptied without saying <em>how</em>, and the ruling is enforced here, once,
    /// for every effect that carries the terminator. An effect terminating on segment expiry would
    /// end Ossify's DR buff every time a ward simply timed out, which is the opposite of the mechanic.
    /// </remarks>
    private static bool Fires(
        DurationTerminator terminator, DurationProbe probe, EffectApplication application) =>
        terminator switch
        {
            DurationTerminator.WARD_BROKEN => probe.OwnerWardEvent == WardPoolEvent.BrokenByDamage,
            _ => throw new EffectContextException(
                terminator.ToString(),
                $"'{application.EffectId}' names an 'until' terminator that 18 §6 does not author",
                "18 §6 authors exactly one: WARD_BROKEN. A second takes 18 §10's route — schema, " +
                "code, document and test in one commit — and this arm is where it announces itself."),
        };

    private static DurationEndReason Reason(
        DurationTerminator terminator, EffectApplication application) =>
        terminator switch
        {
            DurationTerminator.WARD_BROKEN => DurationEndReason.WardBroken,
            _ => throw new EffectContextException(
                terminator.ToString(),
                $"'{application.EffectId}' fired an 'until' terminator with no end reason",
                "18 §6 authors exactly one terminator and DurationEndReason carries exactly one " +
                "answer for it."),
        };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// 🔒 `18` §6's scopes, split by the one question the battle simulator has to ask of them: does this
/// outlive the fight?
/// </summary>
/// <remarks>
/// It is a named predicate rather than a comparison scattered across M2-08, M2-09 and M2-10 because
/// the answer is a <b>ruling</b> (kickoff A4), not a fact about the enum's ordering: <c>STAGE</c>,
/// <c>RUN</c> and <c>PERMANENT</c> belong to a run controller that M3 builds, and the day it arrives
/// this is the one place that has to be read.
/// </remarks>
internal static class DurationScopes
{
    /// <summary>Whether the scope outlives a battle — the run layer's three (kickoff A4).</summary>
    /// <exception cref="EffectContextException">The scope is outside `18` §6's six.</exception>
    internal static bool OutlivesTheBattle(DurationScope scope) =>
        scope switch
        {
            DurationScope.INSTANT or DurationScope.BATTLE or DurationScope.PHASE => false,
            DurationScope.STAGE or DurationScope.RUN or DurationScope.PERMANENT => true,
            _ => throw new EffectContextException(
                scope.ToString(),
                "it is not one of 18 §6's six duration scopes",
                "18 §11: '6 duration scopes = 5 + PHASE'. A seventh has to be placed on one side of " +
                "the battle boundary before anything can ask this."),
        };
}

/// <summary>One applied effect instance, with the bookkeeping `18` §6 needs to end it.</summary>
internal readonly record struct EffectApplication
{
    /// <summary>The effect's `18` §8 id — named in every failure (steering S2).</summary>
    internal required string EffectId { get; init; }

    /// <summary>
    /// `18` §6's duration block; <c>null</c> for `18` §1's canonical passive, which is not
    /// duration-governed.
    /// </summary>
    internal EffectDuration? Duration { get; init; }

    /// <summary>
    /// Battle time at the moment of application. `18` §6's timer runs from here, not from the start
    /// of the fight — `18` §7.10's Ossify is granted on a 14 s <c>PERIODIC</c> and lasts 6 s from
    /// each grant.
    /// </summary>
    internal double AppliedAtSeconds { get; init; }

    /// <summary>
    /// The boss phase current at application; <c>null</c> outside a boss fight, which is what makes
    /// `18` §6's <c>PHASE</c> behave as <c>BATTLE</c> there.
    /// </summary>
    internal int? AppliedInPhase { get; init; }
}

/// <summary>One reading of the battle, taken at the moment a duration is re-evaluated.</summary>
internal readonly record struct DurationProbe
{
    /// <summary>Battle time now. `05` §3 ticks at 20 Hz, so this advances in steps of 0.05.</summary>
    internal required double BattleTimeSeconds { get; init; }

    /// <summary>The boss's current phase; <c>null</c> outside a boss fight.</summary>
    internal int? CurrentPhase { get; init; }

    /// <summary>
    /// 🔒 The ward-pool event that just occurred to the effect's <b>owner</b>, if any — and
    /// <em>how</em> the pool emptied, not merely that it did. See <c>DurationEvaluator.Fires</c>.
    /// </summary>
    internal WardPoolEvent? OwnerWardEvent { get; init; }

    /// <summary>Whether the battle has ended.</summary>
    internal bool BattleEnded { get; init; }
}

/// <summary>Whether an applied effect has ended, and which boundary ended it.</summary>
/// <param name="HasEnded">Whether the effect is over.</param>
/// <param name="Reason">
/// Which `18` §6 boundary ended it, or <see cref="DurationEndReason.NotEnded"/>. Carried so a caller
/// can log the right `05` §7 event — <c>StatusExpired</c> for a timer, and Ossify's terminator for a
/// <c>WardBroken</c> — and so a test can pin <em>which</em> rule fired (steering S2).
/// </param>
internal readonly record struct DurationOutcome(bool HasEnded, DurationEndReason Reason);

/// <summary>
/// 🔒 `05` §4.1 — how a ward pool emptied, which is not one question but two.
/// </summary>
/// <remarks>
/// The two arms are the two halves of `05` §4.1's Events row, and they are a closed set because that
/// row is: <em>"<c>WardBroken</c> the moment the pool reaches 0 through damage… Segment expiry
/// silently removes its remainder (<c>StatusExpired</c>), and does not fire <c>WardBroken</c>."</em>
/// M2-15 already ships both as combat-log events; R17 forbids naming <c>Rules.Combat</c> from here,
/// so this is the same distinction restated at the layer that has to act on it.
/// </remarks>
internal enum WardPoolEvent
{
    /// <summary>
    /// The pool reached 0 <b>through damage</b> — the only thing that fires `05` §4.1's
    /// <c>WardBroken</c>, and the only thing that fires `18` §6's <c>until: WARD_BROKEN</c>.
    /// </summary>
    BrokenByDamage = 1,

    /// <summary>
    /// A segment expired and its remainder was silently removed (<c>StatusExpired</c>). It empties
    /// the pool and terminates nothing.
    /// </summary>
    SegmentExpired = 2,
}

/// <summary>Which `18` §6 boundary ended an effect.</summary>
internal enum DurationEndReason
{
    /// <summary>It has not ended.</summary>
    NotEnded = 0,

    /// <summary>`18` §6's <c>INSTANT</c>: it applied once and did not persist.</summary>
    Instant = 1,

    /// <summary>Its <c>seconds</c> timer ran out.</summary>
    TimerElapsed = 2,

    /// <summary>Its <c>until: WARD_BROKEN</c> terminator fired (`05` §4.1, `18` §7.10's Ossify).</summary>
    WardBroken = 3,

    /// <summary>The boss left the phase the effect was applied in (`18` §6, `05` §3.1's phase check).</summary>
    PhaseExited = 4,

    /// <summary>The battle ended.</summary>
    BattleEnded = 5,
}
