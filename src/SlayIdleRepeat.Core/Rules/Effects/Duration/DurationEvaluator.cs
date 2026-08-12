using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Duration;

/// <summary>🔒 `18` §6 — whether an applied effect has ended, and which boundary ended it.</summary>
internal static class DurationEvaluator
{
    /// <summary>Evaluates one applied effect against one probe of the battle.</summary>
    internal static DurationOutcome Evaluate(EffectApplication application, DurationProbe probe) =>
        throw new NotImplementedException();
}

/// <summary>The `18` §6 scopes that outlive a battle — the run layer's, not the simulator's.</summary>
internal static class DurationScopes
{
    /// <summary>Whether the scope outlives a battle (kickoff A4: declared, not wired).</summary>
    internal static bool OutlivesTheBattle(DurationScope scope) => throw new NotImplementedException();
}

/// <summary>One applied effect instance, with the bookkeeping `18` §6 needs to end it.</summary>
internal readonly record struct EffectApplication
{
    /// <summary>The effect's `18` §8 id — named in every failure (S2).</summary>
    internal required string EffectId { get; init; }

    /// <summary>`18` §6's duration block; <c>null</c> for `18` §1's canonical passive.</summary>
    internal EffectDuration? Duration { get; init; }

    /// <summary>Battle time at the moment of application.</summary>
    internal double AppliedAtSeconds { get; init; }

    /// <summary>The boss phase current at application; <c>null</c> outside a boss fight.</summary>
    internal int? AppliedInPhase { get; init; }
}

/// <summary>One reading of the battle, taken at the moment a duration is re-evaluated.</summary>
internal readonly record struct DurationProbe
{
    /// <summary>Battle time now.</summary>
    internal required double BattleTimeSeconds { get; init; }

    /// <summary>The boss's current phase; <c>null</c> outside a boss fight.</summary>
    internal int? CurrentPhase { get; init; }

    /// <summary>The ward-pool event that just occurred to the effect's owner, if any.</summary>
    internal WardPoolEvent? OwnerWardEvent { get; init; }

    /// <summary>Whether the battle has ended.</summary>
    internal bool BattleEnded { get; init; }
}

/// <summary>Whether an applied effect has ended, and which boundary ended it.</summary>
internal readonly record struct DurationOutcome(bool HasEnded, DurationEndReason Reason);

/// <summary>🔒 `05` §4.1 — how a ward pool emptied, which is not one question but two.</summary>
internal enum WardPoolEvent
{
    /// <summary>The pool reached 0 <b>through damage</b> — the only thing that fires <c>WardBroken</c>.</summary>
    BrokenByDamage = 1,

    /// <summary>A segment expired and its remainder was silently removed (<c>StatusExpired</c>).</summary>
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

    /// <summary>Its <c>until: WARD_BROKEN</c> terminator fired (`05` §4.1).</summary>
    WardBroken = 3,

    /// <summary>The boss left the phase the effect was applied in (`18` §6, `05` §3.1).</summary>
    PhaseExited = 4,

    /// <summary>The battle ended.</summary>
    BattleEnded = 5,
}
