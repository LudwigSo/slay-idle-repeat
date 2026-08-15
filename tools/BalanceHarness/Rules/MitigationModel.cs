using SlayIdleRepeat.BalanceHarness.Content;

namespace SlayIdleRepeat.BalanceHarness.Rules;

/// <summary>The mitigation curve as a closed form, and guardrail 5's ceiling.</summary>
/// <remarks>
/// <para>
/// <code>
/// effDef     = DEF × (1 − attacker.PEN)
/// mitigation = effDef / (effDef + flatConstant + perLevelConstant × attacker.Level)
/// </code>
/// Both dials are read from <c>content/combat_caps.json#/mitigation</c> and not restated here.
/// Evaluated as a closed form rather than by simulation: the curve is strictly increasing in
/// <c>effDef</c> and strictly decreasing in the attacker's level, so enumerating every derivable DEF
/// finds the actual maximum, where sampling fights could only ever find a lower bound.
/// </para>
/// <para>
/// This is a restatement, not bit-identical to the shipped fight: <c>AttackPipeline.Mitigation</c> is
/// unreachable from this tool and applies 4-dp accumulation rounding at two points this copy doesn't
/// (to <c>effDef</c> and to the denominator), so the two curves differ by up to ~1e-8 in the
/// mitigation fraction. Left un-rounded deliberately: the rounded form is only monotone
/// non-decreasing, which would weaken the strict-increase property the closed form's exhaustiveness
/// argument needs. No measured cell comes near that margin.
/// </para>
/// <para>
/// <see cref="Ceiling"/> has no home in <c>game-data/</c> — 0.85 is authored in no tuning or content
/// file, so it is stated once here and the gap is reported rather than closed.
/// </para>
/// </remarks>
public sealed class MitigationModel
{
    /// <summary>Guardrail 5's ceiling — mitigation never exceeds 0.85. Authored nowhere. See the remarks.</summary>
    public const double Ceiling = 0.85;

    private readonly MitigationDials _dials;

    /// <summary>Wraps the authored dials.</summary>
    public MitigationModel(MitigationDials dials)
    {
        ArgumentNullException.ThrowIfNull(dials);
        _dials = dials;
    }

    /// <summary>The dials this model evaluates with.</summary>
    public MitigationDials Dials => _dials;

    /// <summary><c>effDef = DEF × (1 − attacker.PEN)</c>.</summary>
    public static double EffectiveDef(double def, double attackerPen) => def * (1.0 - attackerPen);

    /// <summary>The fraction of an incoming hit the defender's DEF removes.</summary>
    /// <param name="def">The defender's DEF.</param>
    /// <param name="attackerPen">The attacker's PEN.</param>
    /// <param name="attackerLevel">The attacker's level — the <c>20 × Level</c> term.</param>
    public double Mitigation(double def, double attackerPen, int attackerLevel)
    {
        var effDef = EffectiveDef(def, attackerPen);

        return effDef / (effDef + _dials.FlatConstant + (_dials.PerLevelConstant * attackerLevel));
    }

    /// <summary>
    /// The DEF at which the curve reaches a given mitigation, for a given attacker — the inverse, used
    /// by the discriminating control that must <b>breach</b> the ceiling.
    /// </summary>
    /// <remarks>
    /// <c>m = d / (d + k)</c> inverts to <c>d = k·m / (1 − m)</c> with
    /// <c>k = flat + perLevel × attackerLevel</c>, then divided by <c>(1 − PEN)</c> to undo the
    /// penetration. Stated rather than found by search so the control's probe value is exact.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="mitigation"/> is not in [0, 1).</exception>
    public double DefReaching(double mitigation, double attackerPen, int attackerLevel)
    {
        if (mitigation is < 0.0 or >= 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(mitigation),
                mitigation,
                "05 §4's mitigation is a fraction in [0, 1). It reaches 1 only in the limit of infinite " +
                "DEF, so there is no finite DEF for 1.0 or beyond.");
        }

        var constant = _dials.FlatConstant + (_dials.PerLevelConstant * attackerLevel);

        return constant * mitigation / (1.0 - mitigation) / (1.0 - attackerPen);
    }
}
