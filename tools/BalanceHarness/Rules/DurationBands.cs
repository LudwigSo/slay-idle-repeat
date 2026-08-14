namespace SlayIdleRepeat.BalanceHarness.Rules;

/// <summary>
/// 🔒 `17` §1 / `05` §9 — the four fight-duration numbers guardrails 3 and 4 and the Sporequeen band
/// are stated over, restated here because <c>Core</c>'s copy is <c>internal</c>.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>DUPLICATION, deliberately and on the record.</b> These four numbers already exist as
/// <c>SlayIdleRepeat.Core.Rules.Combat.Bosses.BossDurationGuardrails.ParMinSeconds</c> /
/// <c>ParMaxSeconds</c> / <c>HardFloorSeconds</c> / <c>HardCeilingSeconds</c>, with a written
/// rationale for being code rather than data: `17` §1.2 names the band as <em>the arbiter the harness
/// re-tunes the 📐 coefficient rows against</em>, so it is the criterion rather than the variable and
/// carries no 📐 marker. That type is <c>internal</c> and <c>Core</c> grants
/// <c>InternalsVisibleTo</c> to <c>SlayIdleRepeat.Core.Tests</c> alone, so <c>tools/BalanceHarness</c>
/// — the consumer those remarks were written for — cannot reference it and has to say the numbers
/// again. <c>DurationBandsTests</c> pins this copy against `17` §1's literals, and the duplication is
/// reported rather than hidden.
/// </para>
/// <para>
/// `17` §1: <em>"Duration — 35-60 s at par power. Balance guardrail: never below 12 s, never above
/// 70 s (`05` §9)."</em>
/// </para>
/// </remarks>
public static class DurationBands
{
    /// <summary>🔒 `05` §3 — the fixed tick rate. 20 ticks is one second.</summary>
    /// <remarks>
    /// 🔴 <b>A third statement of `05` §3's rate, and it is on the record for the same reason as the
    /// four duration numbers above.</b> <c>Core</c> states it once, in
    /// <c>Core.Primitives.BattleTicks</c>, which is <c>internal</c> and therefore unreachable from
    /// here. <c>DurationBandsTests</c> pins this copy against `05` §3's literals.
    /// </remarks>
    public const double TicksPerSecond = 20.0;

    /// <summary>🔒 `05` §3 — the fight cap. 1800 ticks is 90 s, after which the fight is decided on HP.</summary>
    /// <remarks>
    /// 🔴 <b>Derived rather than written as a bare <c>1800</c>, which is how this copy had already
    /// drifted in shape.</b> <c>Core</c> writes the cap as <c>90 × TicksPerSecond</c> in both of its
    /// statements; this one wrote the product. Nothing was wrong with the number — the hazard is that
    /// a change to the rate would have moved <c>Core</c>'s two and silently left this one behind,
    /// which is precisely the failure a duplicated constant exists to cause.
    /// </remarks>
    public const int MaxFightTicks = 90 * (int)TicksPerSecond;

    /// <summary>🔒 `17` §1 — the bottom of the intended band at par power. Mirrors <c>BossDurationGuardrails.ParMinSeconds</c>.</summary>
    public const double ParMinSeconds = 35.0;

    /// <summary>🔒 `17` §1 — the top of the intended band at par power. Mirrors <c>BossDurationGuardrails.ParMaxSeconds</c>.</summary>
    public const double ParMaxSeconds = 60.0;

    /// <summary>🔒 `17` §1 / `05` §9 — guardrail 3's <em>"never below"</em>. Mirrors <c>BossDurationGuardrails.HardFloorSeconds</c>.</summary>
    public const double HardFloorSeconds = 12.0;

    /// <summary>
    /// 🔒 `17` §1 / `05` §9 — guardrail 4's <em>"never above"</em>. Mirrors
    /// <c>BossDurationGuardrails.HardCeilingSeconds</c>. The same 70 s as the enrage start delay, and
    /// not by coincidence: the enrage is what guarantees termination without a draw, so the ceiling is
    /// the moment that guarantee starts working.
    /// </summary>
    public const double HardCeilingSeconds = 70.0;

    /// <summary>Ticks as seconds.</summary>
    public static double Seconds(int ticks) => ticks / TicksPerSecond;
}
