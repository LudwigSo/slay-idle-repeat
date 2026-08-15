using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// Which phase an HP fraction is in, and what the first-clear extension does to the boundary
/// between phase 1 and phase 2.
/// </summary>
/// <remarks>
/// <para>
/// "Triggered at" 100/66/33% means the comparison is <c>&lt;=</c>: a boss standing exactly on 0.66
/// is in phase 2, and one standing exactly on 0.33 is in phase 3.
/// </para>
/// <para>
/// The first-clear extension is a wider HP band, not more time: a phase is an HP band, and at
/// constant DPS a band's duration is proportional to its width, so "20% longer" is a band 20% wider.
/// Phase 1's authored width is <c>0.34</c>; widened it is <c>0.408</c>, moving the boundary to
/// <c>0.5920</c>. <see cref="Phase3HpFraction"/> is untouched — only phase 1 is extended, so phase 2
/// absorbs the difference.
/// </para>
/// </remarks>
internal static class BossPhaseRules
{
    /// <summary>The phase a boss is in at full HP.</summary>
    internal const int FirstPhase = 1;

    /// <summary>The last phase. There is no phase 4.</summary>
    internal const int FinalPhase = 3;

    /// <summary>The authored phase-2 boundary, on a repeat clear.</summary>
    internal const double AuthoredPhase2HpFraction = 0.66;

    /// <summary>The phase-3 boundary. Unchanged by the first-clear extension.</summary>
    internal const double Phase3HpFraction = 0.33;

    /// <summary>Phase 1's first-clear widening, as a factor on its width.</summary>
    internal const double FirstClearPhase1Widening = 1.20;

    /// <summary>
    /// The HP fraction at or below which the boss is in phase 2 — <see cref="AuthoredPhase2HpFraction"/>
    /// on a repeat clear, and the widened boundary on a first clear.
    /// </summary>
    /// <param name="firstClear">The first-clear flag for this player and this boss.</param>
    /// <returns>The boundary, rounded to four decimals.</returns>
    internal static double Phase2HpFraction(bool firstClear) =>
        firstClear
            ? DeterminismRounding.Round(
                1.0 - (FirstClearPhase1Widening * (1.0 - AuthoredPhase2HpFraction)))
            : AuthoredPhase2HpFraction;

    /// <summary>
    /// The phase an HP fraction is in: <c>hp &lt;= 0.33 ? 3 : hp &lt;= t2 ? 2 : 1</c>.
    /// </summary>
    /// <param name="hpFraction">The boss's HP fraction, <c>0..1</c>.</param>
    /// <param name="firstClear">The first-clear flag.</param>
    /// <returns><see cref="FirstPhase"/>..<see cref="FinalPhase"/>.</returns>
    /// <remarks>
    /// A reading, not a transition. Phases never revert, so <see cref="BossPhaseController"/>
    /// compares this against the phase it is already in and only ever walks upward.
    /// </remarks>
    internal static int PhaseFor(double hpFraction, bool firstClear) =>
        PhaseFor(hpFraction, Phase2HpFraction(firstClear), Phase3HpFraction);

    /// <summary>
    /// The same reading over boundaries a <see cref="BossEncounter"/> already resolved, which is the
    /// form <see cref="BossPhaseController"/> uses.
    /// </summary>
    /// <param name="hpFraction">The boss's HP fraction, <c>0..1</c>.</param>
    /// <param name="phase2HpFraction"><see cref="BossEncounter.Phase2HpFraction"/>.</param>
    /// <param name="phase3HpFraction"><see cref="BossEncounter.Phase3HpFraction"/>.</param>
    /// <returns><see cref="FirstPhase"/>..<see cref="FinalPhase"/>.</returns>
    /// <remarks>
    /// The controller reads the encounter's own boundaries rather than re-deriving them from the
    /// first-clear flag, so the two cannot disagree.
    /// </remarks>
    internal static int PhaseFor(double hpFraction, double phase2HpFraction, double phase3HpFraction) =>
        hpFraction <= phase3HpFraction ? FinalPhase
        : hpFraction <= phase2HpFraction ? FinalPhase - 1
        : FirstPhase;
}
