namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The 4-decimal-place determinism rule — the arithmetic, stated once for the whole assembly.</summary>
/// <remarks>
/// All combat math uses <c>double</c>, rounded to 4 decimal places at every accumulation point —
/// after each damage calculation, each heal, and each stat aggregation step.
/// <para>
/// This consolidates a rounding rule that used to be restated independently at several call sites,
/// two of which had already drifted apart because one omitted the trailing <c>+ 0.0</c> normalisation
/// below and let a <c>-0.0</c> slip through. Callers keep their own exception types and messages —
/// only the rounding arithmetic itself lives here.
/// </para>
/// <para>
/// The trailing <c>+ 0.0</c> is not redundant: <c>Math.Round(-0.00004, 4)</c> is <c>-0.0</c>, and
/// .NET preserves the sign of zero. <c>CanonicalStateWriter</c> throws on a negative zero rather than
/// encoding one, because <c>-0.0 == 0.0</c> is true in C# while the underlying bits differ, so two
/// states the language calls identical would otherwise hash differently.
/// </para>
/// <para>
/// NaN and infinity are not handled here — they are not rounding questions, and only the caller
/// knows which step produced one. Every caller refuses them before calling; <see cref="IsRounded"/>
/// answers <c>false</c> for both so a guard site needs no second check.
/// </para>
/// </remarks>
internal static class DeterminismRounding
{
    /// <summary>The number of decimal places every accumulation point rounds to.</summary>
    internal const int Decimals = 4;

    /// <summary>Rounds one accumulated value to <see cref="Decimals"/> places and normalises <c>-0.0</c> to <c>+0.0</c>.</summary>
    /// <remarks>Total: a NaN rounds to a NaN and an infinity to itself, both unchanged. Refusing them is the caller's job.</remarks>
    internal static double Round(double value) =>
        Math.Round(value, Decimals, MidpointRounding.ToEven) + 0.0;

    /// <summary>True when a value is already in the form <see cref="Round"/> produces: finite, rounded to <see cref="Decimals"/> places, and not a negative zero.</summary>
    /// <remarks>
    /// <c>CanonicalStateWriter</c> and <c>CombatLog</c> do not use this predicate directly: they state
    /// the NaN, infinity, negative-zero and unrounded-value cases as four separate arms so each can
    /// carry its own diagnostic message.
    /// </remarks>
    internal static bool IsRounded(double value) =>
        double.IsFinite(value) &&
        Math.Round(value, Decimals, MidpointRounding.ToEven) == value &&
        !(double.IsNegative(value) && value == 0.0);
}
