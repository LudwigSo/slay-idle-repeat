using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// The state-scaled value: <c>effectiveValue = value × steps</c>, where
/// <c>steps = min( floor( fn / per ), cap )</c>.
/// </summary>
/// <remarks>
/// <para>
/// The 4-dp rounding is on <see cref="Fn"/>'s value, before the division, not on the quotient.
/// Rounding after would put a step boundary in a different place: at <c>per = 0.01</c>, an
/// unrounded <c>0.44999999999999996</c> floors to 44 steps and the rounded <c>0.45</c> to 45 — a
/// one-step difference between a client on ARM64 and a server on x64.
/// </para>
/// <para>
/// This type does not read state: <see cref="StepsFor"/> takes the function's already-evaluated
/// value, since conditions are re-evaluated exactly when needed elsewhere. What lives here is the
/// arithmetic, stated once so call sites cannot each round it differently.
/// </para>
/// <para>
/// A public calculator in <c>Content</c>, stated as an exemption rather than left implicit: this is
/// not a precedent for putting behaviour in <c>Content</c> generally — it is two pure functions of
/// this record's own fields, and nothing here reads state, holds state or resolves anything.
/// </para>
/// </remarks>
public sealed record ValueScale
{
    private readonly double _per;
    private readonly int? _cap;

    /// <summary>Which condition function supplies the state reading.</summary>
    public required ConditionFunction Fn { get; init; }

    /// <summary>State units per step.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Zero or negative. <c>per</c> is the divisor of the formula: zero is a division by zero and a
    /// negative one silently reverses the direction of every step.
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
    /// <exception cref="ArgumentOutOfRangeException">
    /// Negative. A negative cap does not weaken an effect — it inverts it, clamping every reading
    /// to a negative step count and flipping the sign of the whole effect.
    /// </exception>
    public int? Cap
    {
        get => _cap;
        init => _cap = value is null or >= 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(Cap), value,
                "18 §1.1: 'cap' is a maximum number of steps. Negative is not 'no cap' — null is. " +
                "A negative cap inverts every effect it governs.");
    }

    /// <summary>The status <c>HAS_STATUS</c> and <c>STATUS_STACKS</c> read, by status id.</summary>
    /// <remarks>
    /// <para>
    /// Closes a gap the original spec shipped with: <c>STATUS_STACKS</c> takes an argument, but no
    /// field existed to carry it, so a scale over it was unexpressible. Recorded as an erratum.
    /// </para>
    /// <para>
    /// The same keys a condition term already carries, with the same names, types and meanings —
    /// no new vocabulary is introduced.
    /// </para>
    /// <para>
    /// Loose keys rather than the condition evaluator's own arguments type, because that type is
    /// internal under <c>Rules</c> and <c>Content</c> may not name <c>Rules</c>. The seam is still
    /// reused rather than duplicated: a converter turns these into the same arguments the condition
    /// path builds, so a function can never answer differently to a scale than to a condition.
    /// </para>
    /// </remarks>
    public string? StatusId { get; init; }

    /// <summary>
    /// The perk category <c>PERK_COUNT</c> restricts to, optionally. Optional here for the same
    /// reason it is optional there.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// <c>steps = min( floor( fn / per ), cap )</c>, with <paramref name="functionValue"/> rounded
    /// to 4 decimal places before the division.
    /// </summary>
    /// <param name="functionValue">The value <see cref="Fn"/> read from current state.</param>
    /// <returns>
    /// The step count, which may be negative for a negative reading. The formula clamps from above
    /// only, and states no lower bound, so none is imposed here. Every function that drives a
    /// documented scale is non-negative by construction; a negative reading reaching this method
    /// means the function is wrong, and a silent clamp to zero would hide that.
    /// </returns>
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

        // Round the READING, then divide. See the remarks above for why the order is load-bearing.
        var steps = Math.Floor(DeterminismRounding.Round(functionValue) / _per);

        // The cap is applied BEFORE the range check, not after: a capped scale over an enormous
        // reading is well defined, and throwing there would turn a capped effect into a runtime
        // failure at a reading it is explicitly designed to survive.
        if (_cap is { } cap && steps > cap)
        {
            steps = cap;
        }

        if (steps is > int.MaxValue or < int.MinValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(functionValue), functionValue,
                $"18 §1.1: floor({Fn} / {_per.ToString(System.Globalization.CultureInfo.InvariantCulture)}) " +
                $"is {steps.ToString(System.Globalization.CultureInfo.InvariantCulture)}, which is not a step count. " +
                "Either 'per' is far too small for this function's range, or the reading is wrong. " +
                "A cap bounds this from above only, so it is no help to a reading this far below zero.");
        }

        return (int)steps;
    }

    /// <summary>
    /// <c>effectiveValue = value × steps</c>, rounded to 4 decimal places.
    /// </summary>
    /// <param name="value">The effect's authored <c>value</c>.</param>
    /// <param name="functionValue">The value <see cref="Fn"/> read from current state.</param>
    /// <remarks>
    /// A negative authored <c>value</c> multiplied by zero steps gives <c>-0.0</c>, and
    /// <c>Math.Round(-0.0, 4)</c> preserves the sign. <c>CanonicalStateWriter</c> throws on a
    /// negative zero rather than encoding one, since <c>-0.0</c> and <c>0.0</c> have different bit
    /// patterns and would produce two state hashes for one state; this method is the accumulation
    /// point where that gets normalised away. Zero steps is not an edge case: it is the reading at
    /// full HP, at zero gold, and under a cap of zero.
    /// </remarks>
    public double EffectiveValue(double value, double functionValue) =>
        DeterminismRounding.Round(value * StepsFor(functionValue));
}
