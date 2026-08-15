namespace SlayIdleRepeat.BalanceHarness.Rules;

/// <summary>
/// The four fight-duration numbers guardrails 3 and 4 and the Sporequeen band are stated over,
/// restated here because <c>Core</c>'s copy is <c>internal</c>.
/// </summary>
/// <remarks>
/// Deliberate duplication of
/// <c>SlayIdleRepeat.Core.Rules.Combat.Bosses.BossDurationGuardrails</c>'s constants: that type is
/// <c>internal</c> to <c>Core</c> and grants <c>InternalsVisibleTo</c> only to
/// <c>SlayIdleRepeat.Core.Tests</c>, so <c>tools/BalanceHarness</c> cannot reference it and must say
/// the numbers again. <c>DurationBandsTests</c> pins this copy against the authored literals.
/// </remarks>
public static class DurationBands
{
    /// <summary>The fixed tick rate. 20 ticks is one second. Mirrors <c>Core.Primitives.BattleTicks</c>.</summary>
    public const double TicksPerSecond = 20.0;

    /// <summary>
    /// The fight cap. 1800 ticks is 90 s, after which the fight is decided on HP. Derived as
    /// <c>90 × TicksPerSecond</c> (matching <c>Core</c>'s own statement) rather than written as a bare
    /// 1800, so a rate change can't silently leave this copy behind.
    /// </summary>
    public const int MaxFightTicks = 90 * (int)TicksPerSecond;

    /// <summary>The bottom of the intended band at par power. Mirrors <c>BossDurationGuardrails.ParMinSeconds</c>.</summary>
    public const double ParMinSeconds = 35.0;

    /// <summary>The top of the intended band at par power. Mirrors <c>BossDurationGuardrails.ParMaxSeconds</c>.</summary>
    public const double ParMaxSeconds = 60.0;

    /// <summary>Guardrail 3's "never below". Mirrors <c>BossDurationGuardrails.HardFloorSeconds</c>.</summary>
    public const double HardFloorSeconds = 12.0;

    /// <summary>
    /// Guardrail 4's "never above". Mirrors <c>BossDurationGuardrails.HardCeilingSeconds</c>. The same
    /// 70 s as the enrage start delay, not by coincidence: the enrage guarantees termination without a
    /// draw, so the ceiling is the moment that guarantee starts working.
    /// </summary>
    public const double HardCeilingSeconds = 70.0;

    /// <summary>Ticks as seconds.</summary>
    public static double Seconds(int ticks) => ticks / TicksPerSecond;
}
