using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// One row of the archetype table — the whole of what an archetype is.
/// </summary>
/// <remarks>
/// <para>
/// Four coefficients scale a Power-derived stat: <see cref="HpCoef"/>, <see cref="AtkCoef"/>,
/// <see cref="DefCoef"/> and <see cref="AspdCoef"/>. Four secondaries are taken as the stat itself
/// rather than derived: <see cref="Crit"/>, <see cref="CritDamage"/>, <see cref="Dodge"/> and
/// <see cref="Lifesteal"/>.
/// </para>
/// <para>
/// Nothing here has a default — every member is required, since an unstated stat is a bug, not a
/// zero. A defaulted <see cref="HpCoef"/> of 0 gives every enemy of that shape 0 Max HP.
/// </para>
/// <para>
/// <see cref="UnitsPerDraw"/> lives on the row rather than on the pool, because it is a property of
/// the shape: one draw, three bodies for <c>SWARM</c>, wherever the draw came from.
/// </para>
/// </remarks>
/// <param name="Id">Which of the eight shapes this is.</param>
/// <param name="HpCoef"><c>MaxHP = power * hpPerPower * hpCoef</c>.</param>
/// <param name="AtkCoef"><c>ATK = power * atkPerPower * atkCoef</c>.</param>
/// <param name="DefCoef"><c>DEF = power * defPerPower * defCoef</c>.</param>
/// <param name="AspdCoef"><c>ASPD = baseAspd * aspdCoef</c>.</param>
/// <param name="Crit">Crit Chance, taken as the stat.</param>
/// <param name="CritDamage">Crit Damage bonus, taken as the stat.</param>
/// <param name="Dodge">Dodge, taken as the stat.</param>
/// <param name="Lifesteal">Lifesteal, taken as the stat.</param>
/// <param name="UnitsPerDraw">Bodies spawned by one draw of this shape.</param>
/// <param name="OnHit">Which on-hit parameter set this shape carries.</param>
internal sealed record ArchetypeRow(
    EnemyArchetype Id,
    double HpCoef,
    double AtkCoef,
    double DefCoef,
    double AspdCoef,
    double Crit,
    double CritDamage,
    double Dodge,
    double Lifesteal,
    int UnitsPerDraw,
    ArchetypeOnHit OnHit)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{Id} hp×{Format(HpCoef)} atk×{Format(AtkCoef)} def×{Format(DefCoef)} aspd×{Format(AspdCoef)} " +
        $"crit {Format(Crit)} cdmg {Format(CritDamage)} dodge {Format(Dodge)} ls {Format(Lifesteal)} " +
        $"×{InvariantText.Text(UnitsPerDraw)} units, onHit {OnHit}";

    private static string Format(double value) => InvariantText.Text(value);
}
