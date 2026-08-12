using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `17` §1 / `05` §6.3 — which phase an HP fraction is in, and what the first-clear extension
/// does to the boundary between phase 1 and phase 2.
/// </summary>
/// <remarks>
/// <para>
/// `17` §1: <em>"Phases — exactly 3, triggered at 100%, 66% and 33% Max HP."</em> <b>Triggered
/// AT</b>, so the comparison is <c>&lt;=</c>: a boss standing exactly on 0.66 is in phase 2, and one
/// standing exactly on 0.33 is in phase 3.
/// </para>
/// <para>
/// ═══ 🔒 <b>THE FIRST-CLEAR EXTENSION IS A WIDER HP BAND, NOT MORE TIME</b> ═══
/// </para>
/// <para>
/// `17` §1: <em>"The first time a player fights a boss, phase 1 lasts 20% longer, to let them read
/// the fight."</em> A phase is an <b>HP band</b>, and at constant DPS a band's duration is
/// proportional to its width — so <em>"20% longer"</em> is a band 20% wider. Phase 1's authored width
/// is <c>1.00 − 0.66 = 0.34</c>; widened it is <c>0.408</c>, and the boundary moves to
/// <c>1.0 − 0.408 = 0.5920</c>. <see cref="Phase3HpFraction"/> is untouched: only phase 1 is
/// extended, so phase 2 absorbs the difference.
/// </para>
/// <para>
/// 🔴 <b><c>CombatRules.MaxTicks</c> is NOT the mechanism, and <c>BattleSeams</c> said it was.</b>
/// That remark is corrected in place: <c>CombatRules.PvE.MaxTicks</c> already equals
/// <c>CombatLog.MaxTicks</c> (1800) and <c>BattlePlan.Validated</c> throws above it, so there is no
/// headroom to extend into — and extending the fight would lengthen the <em>whole</em> fight rather
/// than phase 1. No tunable is added, no HP is added, and the 70 s enrage is unmoved.
/// </para>
/// </remarks>
internal static class BossPhaseRules
{
    /// <summary>🔒 `17` §1 — the phase a boss is in at full HP.</summary>
    internal const int FirstPhase = 1;

    /// <summary>🔒 `17` §1 — the last phase. There is no phase 4.</summary>
    internal const int FinalPhase = 3;

    /// <summary>🔒 `17` §1 — the authored phase-2 boundary, on a repeat clear.</summary>
    internal const double AuthoredPhase2HpFraction = 0.66;

    /// <summary>🔒 `17` §1 — the phase-3 boundary. Unchanged by the first-clear extension.</summary>
    internal const double Phase3HpFraction = 0.33;

    /// <summary>🔒 `17` §1 — <em>"phase 1 lasts 20% longer"</em>, as a factor on phase 1's width.</summary>
    internal const double FirstClearPhase1Widening = 1.20;

    /// <summary>
    /// 🔒 The HP fraction at or below which the boss is in phase 2 — <see cref="AuthoredPhase2HpFraction"/>
    /// on a repeat clear, and the widened boundary on a first clear.
    /// </summary>
    /// <param name="firstClear">`17` §1's first-clear flag for this player and this boss.</param>
    /// <returns>The boundary, rounded to `05` §1.1's four decimals.</returns>
    /// <remarks>
    /// 🔴 <b>PHASE 1b STUB — the boss engine's implementation phase owns the body.</b>
    /// </remarks>
    /// <exception cref="NotSupportedException">Always, until M2-12's implementation phase lands.</exception>
    internal static double Phase2HpFraction(bool firstClear) =>
        throw new NotSupportedException(
            "BossPhaseRules.Phase2HpFraction is declared and not written yet — M2-12's IMPLEMENTATION " +
            "phase owns the body. It is `17` §1's 0.66 on a repeat clear, and on a first clear the " +
            "phase-1 band widened by 20%: DeterminismRounding.Round(1.0 - 1.20 * (1.0 - 0.66)) = " +
            $"0.5920 (firstClear was {firstClear.ToString(CultureInfo.InvariantCulture)}). Returning " +
            "0.66 unconditionally would silently delete the first-clear extension, which no log would " +
            "show (steering S6).");

    /// <summary>
    /// 🔒 `17` §1 — the phase an HP fraction is in: <c>hp &lt;= 0.33 ? 3 : hp &lt;= t2 ? 2 : 1</c>.
    /// </summary>
    /// <param name="hpFraction">The boss's HP fraction, <c>0..1</c>.</param>
    /// <param name="firstClear">`17` §1's first-clear flag.</param>
    /// <returns><see cref="FirstPhase"/>..<see cref="FinalPhase"/>.</returns>
    /// <remarks>
    /// 🔒 It is a <b>reading</b>, not a transition. Phases never revert (`05` §3.1), so
    /// <see cref="BossPhaseController"/> compares this against the phase it is already in and only
    /// ever walks upward — a boss healed back above a threshold does not re-enter.
    /// <para>
    /// 🔴 <b>PHASE 1b STUB — the boss engine's implementation phase owns the body.</b>
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">Always, until M2-12's implementation phase lands.</exception>
    internal static int PhaseFor(double hpFraction, bool firstClear) =>
        throw new NotSupportedException(
            "BossPhaseRules.PhaseFor is declared and not written yet — M2-12's IMPLEMENTATION phase " +
            "owns the body. `17` §1 triggers the phases AT 100%, 66% and 33%, so the comparison is " +
            "<= and a boss standing exactly on a threshold is already in the lower phase. It was asked " +
            $"for {hpFraction.ToString("R", CultureInfo.InvariantCulture)} with firstClear " +
            $"{firstClear.ToString(CultureInfo.InvariantCulture)}. Answering 1 for everything would " +
            "make every boss a one-phase fight with nothing going red (steering S6).");
}
