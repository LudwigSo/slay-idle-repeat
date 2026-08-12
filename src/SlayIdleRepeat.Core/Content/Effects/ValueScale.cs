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
/// <para>
/// ⚠️ <b>A public calculator in <c>Content</c>, stated as an exemption rather than left implicit.</b>
/// `30` §11.2 rules calculators <c>internal</c> and under <c>Rules</c>, with two named public
/// exceptions each carrying a documented external consumer. <see cref="StepsFor"/> and
/// <see cref="EffectiveValue"/> are a third, on the same terms: the named consumers are M2-02's
/// resolver, M2-05's condition evaluator and the balance harness (`05` §9), which runs against
/// synthetic stat blocks and loads no aggregate. The alternative — the formula restated in each —
/// is the duplication `18` §8 exists to prevent, and the rounding order below is precisely the
/// detail three independent restatements would get differently. This is <b>not</b> a precedent for
/// putting behaviour in <c>Content</c>: it is two pure functions of this record's own fields, and
/// nothing here reads state, holds state or resolves anything.
/// </para>
/// </remarks>
public sealed record ValueScale
{
    private readonly double _per;
    private readonly int? _cap;

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
    /// <exception cref="ArgumentOutOfRangeException">
    /// Negative. <c>game-data/schema/effect.schema.json</c> declares <c>cap</c> with
    /// <c>"minimum": 0</c>, and a negative cap does not weaken an effect — it inverts it, clamping
    /// every reading to a negative step count and flipping the sign of the whole effect. The two
    /// statements of the bound have to agree, or a scale built in code (the balance harness, a
    /// test) can reach a state no authored JSON can.
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

    /// <summary>
    /// `18` §1.1's <c>steps = min( floor( fn / per ), cap )</c>, with <paramref name="functionValue"/>
    /// rounded to 4 decimal places before the division.
    /// </summary>
    /// <param name="functionValue">The value <see cref="Fn"/> read from current state.</param>
    /// <returns>
    /// The step count, which may be <b>negative</b> for a negative reading. `18` §1.1's formula
    /// clamps from above only — <c>min(…, cap)</c> — and states no lower bound, so none is imposed
    /// here (`16` R6: a bound the design has not authorised is not one to invent). Every §4 function
    /// that drives a documented scale is non-negative by construction; a negative reading reaching
    /// this method means the function is wrong, and a silent clamp to zero would hide that.
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

        // 🔒 Round the READING, then divide. See the remarks above for why the order is load-bearing.
        var steps = Math.Floor(Math.Round(functionValue, 4) / _per);

        // 🔒 The cap is applied BEFORE the range check, not after. A capped scale over an enormous
        // reading is well defined — `min(…, cap)` is the cap — and throwing there would turn
        // PK_BERSERK into a runtime failure at a reading it is explicitly designed to survive.
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
    /// `18` §1.1's <c>effectiveValue = value × steps</c>, rounded to 4 decimal places (`05` §1.1,
    /// `14` §8.2, `18` §8 step 10).
    /// </summary>
    /// <param name="value">The effect's authored <c>value</c>.</param>
    /// <param name="functionValue">The value <see cref="Fn"/> read from current state.</param>
    /// <remarks>
    /// 🔒 <b>The trailing <c>+ 0.0</c> is not redundant.</b> A negative authored <c>value</c> — `18`
    /// §7.10's Bog Air is <c>-0.35</c>, and every §7.5-style drawback is negative — multiplied by
    /// <b>zero steps</b> gives <c>-0.0</c>, and <c>Math.Round(-0.0, 4)</c> preserves the sign.
    /// <c>CanonicalStateWriter</c> <em>throws</em> on a negative zero rather than encoding one,
    /// because <c>-0.0</c> and <c>0.0</c> have different bit patterns and would produce two
    /// <c>stateHash</c>es for one state; its own comment names the fix and names the owner —
    /// <em>"normalise at the accumulation point — <c>x + 0.0</c> is +0.0 — rather than letting the
    /// writer edit state on its way out."</em> This is that accumulation point (`18` §8 step 10).
    /// Zero steps is not an edge case: it is the reading at full HP, at zero gold, and under a cap
    /// of zero.
    /// </remarks>
    public double EffectiveValue(double value, double functionValue) =>
        Math.Round(value * StepsFor(functionValue), 4) + 0.0;
}
