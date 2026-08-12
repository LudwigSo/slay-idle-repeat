namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// 🔒 `18` §1.1 — the state-scaled value: <c>effectiveValue = value × steps</c>, where
/// <c>steps = min( floor( fn / per ), cap )</c>.
/// </summary>
/// <remarks>
/// <para>
/// The two worked rows `18` §1.1 gives are <c>PK_BERSERK</c> —
/// <c>{"fn":"SELF_MISSING_HP_PCT","per":0.01,"cap":45}</c> — and <c>PK_HOARD</c> —
/// <c>{"fn":"GOLD_HELD","per":100,"cap":null}</c>, uncapped.
/// </para>
/// <para>
/// 🔒 <b>The 4-dp rounding is on <see cref="Fn"/>'s value, <em>before</em> the division</b>, not on
/// the quotient (`18` §1.1's table, and `05` §1.1's determinism rule). Rounding after would put a
/// step boundary in a different place: at <c>per = 0.01</c>, an unrounded <c>0.44999999999999996</c>
/// floors to 44 steps and the rounded <c>0.45</c> to 45 — a one-step difference between a client on
/// ARM64 and a server on x64, which is exactly the divergence `14` §8.2 exists to prevent.
/// </para>
/// <para>
/// This type does not read state. <see cref="StepsFor"/> takes the function's already-evaluated
/// value, because <em>"<c>valueScale</c> is re-evaluated exactly when conditions are (§4): at every
/// resolution pass for <c>ALWAYS</c> effects, at fire time for triggered ones"</em> — and who
/// evaluates §4 is M2-05's, not M2-01's. What lives here is the arithmetic, stated once so that
/// sixteen call sites cannot each round it differently.
/// </para>
/// </remarks>
public sealed record ValueScale
{
    private readonly double _per;

    /// <summary>
    /// Which of `18` §4's condition functions supplies the state reading.
    /// <em>"Any condition function from §4."</em>
    /// </summary>
    public required ConditionFunction Fn { get; init; }

    /// <summary>State units per step.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Zero or negative. <c>per</c> is the divisor of `18` §1.1's formula: zero is a division by
    /// zero and a negative one silently reverses the direction of every step, so neither is a
    /// value to accept and interpret later.
    /// </exception>
    public required double Per
    {
        get => _per;
        init => _per = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(Per), value,
                "18 §1.1: 'per' is the divisor of steps = floor(fn / per). It must be greater than zero.");
    }

    /// <summary>The maximum number of steps; <c>null</c> is uncapped.</summary>
    public int? Cap { get; init; }

    /// <summary>
    /// `18` §1.1's <c>steps = min( floor( fn / per ), cap )</c>, with <paramref name="functionValue"/>
    /// rounded to 4 decimal places before the division.
    /// </summary>
    /// <param name="functionValue">The value <see cref="Fn"/> read from current state.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="functionValue"/> is NaN or infinite, or the step count does not fit in an
    /// <see cref="int"/>. Both would otherwise become a silently wrong multiplier.
    /// </exception>
    public int StepsFor(double functionValue)
    {
        if (double.IsNaN(functionValue) || double.IsInfinity(functionValue))
        {
            throw new ArgumentOutOfRangeException(
                nameof(functionValue), functionValue,
                $"18 §4 condition functions are finite readings of state; {Fn} produced a value that is not.");
        }

        // 🔒 Round the READING, then divide. See the remarks above for why the order is load-bearing.
        var steps = Math.Floor(Math.Round(functionValue, 4) / _per);

        if (Cap is { } cap && steps > cap)
        {
            steps = cap;
        }

        if (steps is > int.MaxValue or < int.MinValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(functionValue), functionValue,
                $"18 §1.1: floor({Fn} / {_per.ToString(System.Globalization.CultureInfo.InvariantCulture)}) " +
                $"is {steps.ToString(System.Globalization.CultureInfo.InvariantCulture)}, which is not a step count. " +
                "Either 'per' is far too small for this function's range or the reading is wrong; " +
                "an uncapped valueScale over an unbounded function needs a cap.");
        }

        return (int)steps;
    }

    /// <summary>
    /// `18` §1.1's <c>effectiveValue = value × steps</c>, rounded to 4 decimal places (`05` §1.1,
    /// `14` §8.2, `18` §8 step 10).
    /// </summary>
    /// <param name="value">The effect's authored <c>value</c>.</param>
    /// <param name="functionValue">The value <see cref="Fn"/> read from current state.</param>
    public double EffectiveValue(double value, double functionValue) =>
        Math.Round(value * StepsFor(functionValue), 4);
}
