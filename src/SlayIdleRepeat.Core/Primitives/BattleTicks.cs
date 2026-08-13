namespace SlayIdleRepeat.Core.Primitives;

/// <summary>
/// 🔒 <b>`05` §3's fixed-tick rule — the tick rate, the fight cap and the whole-tick predicate,
/// stated once for the whole assembly.</b>
/// </summary>
/// <remarks>
/// <para>
/// `05` §3: the simulation is fixed-tick at <c>TICK = 0.05 s</c> — 20 ticks per second — and a
/// fight is capped at 90 s, so ticks run <c>0..1799</c>. A trigger, a telegraph and a duration all
/// fire <b>on</b> a tick, which is what makes a fractional span meaningless rather than merely
/// imprecise: it points between two ticks and therefore at neither.
/// </para>
/// <para>
/// 🔒 <b>Why this type exists.</b> The rule had three independent statements before cross-task
/// review — <c>Rules/Effects/Triggers/TriggerSchedule</c>, <c>Rules/Combat/CombatLog</c> and
/// <c>tools/BalanceHarness/Rules/DurationBands</c> — each carrying its own <c>20</c>, its own
/// <c>1e-9</c> tolerance, its own <c>Math.Abs(x - Math.Round(x)) &gt; tolerance</c> predicate and its
/// own <em>"Use a multiple of 0.05 s"</em> sentence. <c>TriggerSchedule</c> said so itself:
/// <em>"the same `05` §3 fact is also stated by <c>CombatLog.TicksPerSecond</c>, and this file
/// cannot name it."</em> That is <b>exactly</b> the reasoning that made <c>OpRounding</c> a second
/// statement of <c>StatRounding</c>, and <see cref="DeterminismRounding"/> is the precedent for the
/// fix rather than a new idea.
/// </para>
/// <para>
/// ⚠️ <b>Only the rate was pinned by a test.</b>
/// <c>PeriodicAnchoringTests.The_tick_rate_agrees_with_the_combat_log</c> closed the <c>20</c> across
/// the first two. The <b>tolerance</b>, the <b>predicate</b>, the <b>90 s cap</b> and the
/// <b>message</b> were not closed by anything — and the harness's copy had already drifted in shape,
/// writing its cap as a bare <c>1800</c> rather than as <c>90 × TicksPerSecond</c>, so a change to
/// the rate would have moved two of the three statements and silently left the third behind.
/// </para>
/// <para>
/// 🔒 <b><c>Primitives</c>, and it has to be.</b> R17 makes <c>Rules.Effects</c> the bottom of the
/// intra-<c>Rules</c> layering, so it cannot reach <c>Rules.Combat</c> — which is <em>why</em>
/// <c>TriggerSchedule</c> was written as a second statement. `30` §11.4's dependency direction is
/// <c>Handlers ▶ Rules ▶ Model ▶ Content ▶ Primitives</c>, so <c>Primitives</c> is the one place
/// every layer that counts ticks can name, including the harness, which `30` §6 pins to
/// <c>Core</c> only.
/// </para>
/// <para>
/// ⚠️ <b>The callers keep their own failure types and their own wording, and that is not a second
/// statement of the rule.</b> <c>TriggerSchedule</c> throws an <c>EffectContextException</c> naming
/// the trigger and the `18` §3 parameter; <c>CombatLog</c> throws an <c>InvalidOperationException</c>
/// naming the telegraph and its tick. Both are load-bearing — steering S2 asks <em>which</em> rule
/// fired — and neither is arithmetic. What is consolidated here is the <b>rate</b>, the <b>cap</b>,
/// the <b>tolerance</b> and the <b>predicate</b>: the four things whose disagreement would let one
/// caller accept a span another refuses.
/// </para>
/// </remarks>
internal static class BattleTicks
{
    /// <summary>🔒 `05` §3 — the tick rate: <c>TICK = 0.05 s</c>, 20 ticks per second.</summary>
    internal const int PerSecond = 20;

    /// <summary>
    /// 🔒 `05` §3 — the PvE fight cap: 90 s at 20 ticks/second. Ticks run <c>0..1799</c>.
    /// </summary>
    /// <remarks>
    /// ⚠️ The <b>PvE</b> cap. `05` §3.3 gives a duel a 60 s cap (<c>pvpMaxFightSeconds</c>, `11`
    /// §4.3) — 1200 ticks — which is a <em>rules</em> choice per fight and not this constant, so it
    /// is carried by <c>CombatRules</c> rather than restated here.
    /// </remarks>
    internal const int MaxPerFight = 90 * PerSecond;

    /// <summary>
    /// How far a span may sit from a whole tick before it is a fractional span rather than the
    /// residue of multiplying a decimal by 20.
    /// </summary>
    /// <remarks>
    /// 🔒 Sized against the two things it stands between: <c>1.2 × 20</c> is not bit-exactly
    /// <c>24.0</c> for every value in `17` §1's telegraph band, while the defect being caught — a
    /// lead of <c>1.0001 s</c>, which is <c>20.002</c> ticks — misses by <c>2e-3</c>, six orders of
    /// magnitude outside anything rounding can explain.
    /// </remarks>
    internal const double WholeTickTolerance = 1e-9;

    /// <summary>🔒 One tick, in seconds — what a refusal tells the author to use a multiple of.</summary>
    internal const double SecondsPerTick = 1.0 / PerSecond;

    /// <summary>
    /// 🔒 `05` §3 — whether a span of seconds lands on a whole tick, and what that tick count is.
    /// </summary>
    /// <remarks>
    /// Answers <c>false</c> for a NaN or an infinity as well as for a fractional span: both are
    /// spans a fixed-tick simulation cannot place, and every caller refuses them. Callers that need
    /// to tell the two apart — <c>TriggerSchedule</c> does, because an arithmetic failure upstream
    /// and a mis-authored decimal want different sentences — check finiteness first and use this for
    /// the whole-tick question alone.
    /// </remarks>
    /// <param name="seconds">The span, in seconds.</param>
    /// <param name="ticks">The span in ticks, rounded — meaningful only when this returns <c>true</c>.</param>
    /// <returns><c>true</c> when the span is a whole number of ticks.</returns>
    internal static bool IsWhole(double seconds, out double ticks)
    {
        ticks = seconds * PerSecond;

        if (double.IsNaN(ticks) || double.IsInfinity(ticks))
        {
            return false;
        }

        var whole = Math.Round(ticks);
        var isWhole = Math.Abs(ticks - whole) <= WholeTickTolerance;

        if (isWhole)
        {
            ticks = whole;
        }

        return isWhole;
    }
}
