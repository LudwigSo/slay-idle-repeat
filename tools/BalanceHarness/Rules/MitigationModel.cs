using SlayIdleRepeat.BalanceHarness.Content;

namespace SlayIdleRepeat.BalanceHarness.Rules;

/// <summary>
/// 🔒 `05` §4 step 3's mitigation curve as a closed form, and guardrail 5's ceiling.
/// </summary>
/// <remarks>
/// <para>
/// <code>
/// effDef     = DEF × (1 − attacker.PEN)
/// mitigation = effDef / (effDef + flatConstant + perLevelConstant × attacker.Level)
/// </code>
/// Both dials are read from <c>content/combat_caps.json#/mitigation</c> — `05` §4 calls them
/// <em>"the two most important balance dials in the game"</em> and neither is restated here.
/// </para>
/// <para>
/// 🔒 <b>Guardrail 5 is a closed form and not a simulation, and that is what makes it exhaustive.</b>
/// The curve is strictly increasing in <c>effDef</c> and strictly decreasing in the attacker's level,
/// so the maximum over a chapter is reached at its <em>highest</em> DEF and <em>lowest</em> attacker
/// level. Sampling fights could only ever find a lower bound; evaluating the form over every derivable
/// DEF finds the actual maximum, which is what <em>"never exceeds 0.85 for any reachable DEF at any
/// chapter"</em> asks for.
/// </para>
/// <para>
/// 🔴 <b>ERRATUM — <see cref="Ceiling"/> has no home in <c>game-data/</c>.</b> The 0.85 is `05` §9's
/// and is authored in no tuning or content file; <c>combat_caps.json</c> carries the two dials and
/// the six stat caps but no mitigation ceiling. It is stated once, named and cited here, and the gap
/// is reported rather than closed by adding a number to a file this milestone may not edit.
/// </para>
/// </remarks>
public sealed class MitigationModel
{
    /// <summary>🔴 📐 `05` §9 guardrail 5 — <em>"mitigation never exceeds 0.85"</em>. Authored nowhere. See the remarks.</summary>
    public const double Ceiling = 0.85;

    private readonly MitigationDials _dials;

    /// <summary>Wraps the authored dials.</summary>
    public MitigationModel(MitigationDials dials)
    {
        ArgumentNullException.ThrowIfNull(dials);
        _dials = dials;
    }

    /// <summary>`05` §4 — the dials this model evaluates with.</summary>
    public MitigationDials Dials => _dials;

    /// <summary>🔒 `05` §4 step 3 — <c>effDef = DEF × (1 − attacker.PEN)</c>.</summary>
    public static double EffectiveDef(double def, double attackerPen) => def * (1.0 - attackerPen);

    /// <summary>
    /// 🔒 `05` §4 step 3 — the fraction of an incoming hit the defender's DEF removes.
    /// </summary>
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
