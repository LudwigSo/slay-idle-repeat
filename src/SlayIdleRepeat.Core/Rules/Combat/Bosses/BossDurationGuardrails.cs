namespace SlayIdleRepeat.Core.Rules.Combat.Bosses;

/// <summary>
/// 🔒 `17` §1 / `05` §9 — the fight-duration band a boss is tuned to, stated as named numbers so the
/// balance harness and the documents cannot drift apart.
/// </summary>
/// <remarks>
/// <para>
/// `17` §1: <em>"Duration — 35–60 s at par power. Balance guardrail: never below 12 s, never above
/// 70 s (`05` §9)."</em>
/// </para>
/// <para>
/// 🔒 <b>These are <em>balance-harness</em> assertions, not engine invariants, and nothing here
/// throws.</b> A fight that runs 8 s is a tuning failure the harness (M2-16a) reports against a
/// whole distribution; an engine that refused it would turn a balance finding into a crash in the
/// middle of a player's run, and would make the harness unable to measure the very thing it exists
/// to measure.
/// </para>
/// <para>
/// ⚠️ <b>Nothing in production reads these, and that is the point rather than an oversight.</b> The
/// consumer is M2-16a's harness, which has not landed; until it does, the only readers are the
/// transcription tests that pin the four numbers against `17` §1. They are code rather than data
/// because `17` §1.2 names this band as <em>the arbiter the harness re-tunes the 📐 coefficient rows
/// against</em> — it is the criterion, not the variable, and none of the four carries a 📐 marker in
/// `17` §1 or `05` §9. The 📐 numbers a boss does own — §1.2's per-boss coefficients and §10's reward
/// rows — are M2-13's, in <c>game-data/content/bosses/</c>, and are deliberately not here.
/// </para>
/// </remarks>
internal static class BossDurationGuardrails
{
    /// <summary>🔒 `17` §1 — the bottom of the intended band at par power.</summary>
    internal const double ParMinSeconds = 35.0;

    /// <summary>🔒 `17` §1 — the top of the intended band at par power.</summary>
    internal const double ParMaxSeconds = 60.0;

    /// <summary>🔒 `17` §1 / `05` §9 — <em>"never below"</em>.</summary>
    internal const double HardFloorSeconds = 12.0;

    /// <summary>
    /// 🔒 `17` §1 / `05` §9 — <em>"never above"</em>. The same 70 s as
    /// <see cref="BossBuiltIns.EnrageStartDelaySeconds"/>, and not by coincidence: `17` §1 states
    /// the enrage <em>"guarantees termination without a draw"</em>, so the ceiling is the moment the
    /// guarantee starts working.
    /// </summary>
    internal const double HardCeilingSeconds = 70.0;
}
