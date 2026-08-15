using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Duration;

/// <summary>
/// Whether an applied effect has ended, and which boundary ended it.
/// </summary>
/// <remarks>
/// A pure function of one application and one probe — no timers, no registry, no clock, so
/// different drivers of the tick loop can't disagree about how remaining time is stored. Checks run
/// in order: <c>INSTANT</c> first (it never persists to be timed), then the <c>until</c> terminator
/// and the timer together — when both are true on the same probe the terminator wins, since it's the
/// instantaneous event while the timer is a threshold that stays crossed — then the scope.
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
        // A null duration means "not duration-governed" (e.g. an ALWAYS passive), not INSTANT —
        // reading it as INSTANT would end every passive on the tick it was applied.
        if (application.Duration is not { } duration)
        {
            return Running;
        }

        if (duration.Scope == DurationScope.INSTANT)
        {
            return new DurationOutcome(true, DurationEndReason.Instant);
        }

        if (duration.Until is { } terminator && Fired(terminator, probe, application) is { } fired)
        {
            return new DurationOutcome(true, fired);
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

            // These three outlive a battle by definition, so the battle-scoped evaluator never ends
            // one — a run-level controller does, keyed on DurationScopes.OutlivesTheBattle.
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
    /// <c>PHASE</c> ends when the boss exits the phase the effect was applied in; outside a boss
    /// fight it behaves as <c>BATTLE</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="EffectApplication.AppliedInPhase"/> is the sole discriminator of "inside a boss
    /// fight" — not the probe's current phase and not a separate flag — so there's one source of
    /// truth for the fallback rather than two that a caller could set inconsistently.
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

        // Still inside the applied phase, so the phase boundary hasn't fired — but PHASE is
        // battle-bounded, so the battle's own boundary still ends it if the fight is over.
        return current > appliedIn
            ? new DurationOutcome(true, DurationEndReason.PhaseExited)
            : Battle(probe);
    }

    /// <summary>
    /// <c>WARD_BROKEN</c> fires only when the ward pool reaches 0 through damage — not on segment
    /// expiry, which silently removes the remainder instead.
    /// </summary>
    /// <remarks>
    /// <see cref="DurationProbe.OwnerWardEvent"/> is a <see cref="WardPoolEvent"/> rather than a
    /// <c>bool</c> so a caller can't report the pool emptying without saying how — a terminator wired
    /// to a plain "broke" flag would end an effect on a segment simply timing out, which is the
    /// opposite of the mechanic.
    /// </remarks>
    /// <returns>The end reason when the terminator fired, or <c>null</c> when it did not.</returns>
    private static DurationEndReason? Fired(
        DurationTerminator terminator, DurationProbe probe, EffectApplication application) =>
        terminator switch
        {
            DurationTerminator.WARD_BROKEN => probe.OwnerWardEvent == WardPoolEvent.BrokenByDamage
                ? DurationEndReason.WardBroken
                : null,
            _ => throw new EffectContextException(
                terminator.ToString(),
                $"'{application.EffectId}' names an 'until' terminator that 18 §6 does not author",
                "18 §6 authors exactly one: WARD_BROKEN. A second takes 18 §10's route — schema, " +
                "code, document and test in one commit — and this arm is where it announces itself."),
        };

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// The duration scopes, split by the one question the battle simulator has to ask of them: does this
/// outlive the fight?
/// </summary>
/// <remarks>
/// A named predicate rather than a comparison scattered across every caller, since <c>STAGE</c>,
/// <c>RUN</c> and <c>PERMANENT</c> belong to a run controller not yet built, and this is the one
/// place that needs to be read once it lands. Tracked as a pending case in
/// <c>SubjectSetFloorTests.Pending</c> under the name <c>RunController</c> until then.
/// </remarks>
internal static class DurationScopes
{
    /// <summary>Whether the scope outlives a battle.</summary>
    /// <exception cref="EffectContextException">The scope is not a recognized duration scope.</exception>
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

/// <summary>One applied effect instance, with the bookkeeping needed to end it.</summary>
internal readonly record struct EffectApplication
{
    /// <summary>The effect's id — named in every failure.</summary>
    internal required string EffectId { get; init; }

    /// <summary>The duration block; <c>null</c> for a passive that isn't duration-governed.</summary>
    internal EffectDuration? Duration { get; init; }

    /// <summary>Battle time at the moment of application. The timer runs from here, not from the fight's start.</summary>
    internal double AppliedAtSeconds { get; init; }

    /// <summary>
    /// The boss phase current at application; <c>null</c> outside a boss fight, which is what makes
    /// <c>PHASE</c> behave as <c>BATTLE</c> there.
    /// </summary>
    internal int? AppliedInPhase { get; init; }
}

/// <summary>One reading of the battle, taken at the moment a duration is re-evaluated.</summary>
internal readonly record struct DurationProbe
{
    /// <summary>Battle time now. Ticks at 20 Hz, so this advances in steps of 0.05.</summary>
    internal required double BattleTimeSeconds { get; init; }

    /// <summary>The boss's current phase; <c>null</c> outside a boss fight.</summary>
    internal int? CurrentPhase { get; init; }

    /// <summary>
    /// The ward-pool event that just occurred to the effect's owner, if any — and how the pool
    /// emptied, not merely that it did. See <c>DurationEvaluator.Fired</c>.
    /// </summary>
    internal WardPoolEvent? OwnerWardEvent { get; init; }

    /// <summary>Whether the battle has ended.</summary>
    internal bool BattleEnded { get; init; }
}

/// <summary>Whether an applied effect has ended, and which boundary ended it.</summary>
/// <param name="HasEnded">Whether the effect is over.</param>
/// <param name="Reason">
/// Which boundary ended it, or <see cref="DurationEndReason.NotEnded"/>. Carried so a caller can log
/// the right combat event and so a test can pin which rule fired.
/// </param>
internal readonly record struct DurationOutcome(bool HasEnded, DurationEndReason Reason);

/// <summary>How a ward pool emptied — not one question but two, and a closed set.</summary>
internal enum WardPoolEvent
{
    /// <summary>The pool reached 0 through damage — the only thing that fires <c>WARD_BROKEN</c>.</summary>
    BrokenByDamage = 1,

    /// <summary>A segment expired and its remainder was silently removed. Terminates nothing.</summary>
    SegmentExpired = 2,
}

/// <summary>Which boundary ended an effect.</summary>
internal enum DurationEndReason
{
    /// <summary>It has not ended.</summary>
    NotEnded = 0,

    /// <summary><c>INSTANT</c>: it applied once and did not persist.</summary>
    Instant = 1,

    /// <summary>Its <c>seconds</c> timer ran out.</summary>
    TimerElapsed = 2,

    /// <summary>Its <c>until: WARD_BROKEN</c> terminator fired.</summary>
    WardBroken = 3,

    /// <summary>The boss left the phase the effect was applied in.</summary>
    PhaseExited = 4,

    /// <summary>The battle ended.</summary>
    BattleEnded = 5,
}
