using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Combat.Enemies;

/// <summary>
/// 🔒 One row of `05` §6.1's archetype table — the whole of what an archetype <em>is</em>.
/// </summary>
/// <remarks>
/// <para>
/// Four coefficients scale a Power-derived stat (`05` §6): <see cref="HpCoef"/>,
/// <see cref="AtkCoef"/>, <see cref="DefCoef"/> and <see cref="AspdCoef"/>. Four secondaries are
/// taken as the stat itself rather than derived: <see cref="Crit"/>, <see cref="CritDamage"/>,
/// <see cref="Dodge"/> and <see cref="Lifesteal"/>. `05` §6.1 preserves three of those exactly from
/// earlier prose — <c>SKIRMISHER</c> dodge 0.15, <c>LEECH</c> lifesteal 0.25 and <c>REAVER</c> crit
/// 0.30 / critDamage 1.20 — and the rest are authored per `16` A7.
/// </para>
/// <para>
/// 🔒 <b>Nothing here has a default.</b> Every member is required, because `05` §2's ruling that
/// <em>"an unstated stat is a bug, not a zero"</em> is exactly as true of the coefficient that
/// produces the stat as of the stat. A defaulted <see cref="HpCoef"/> of 0 gives every enemy of that
/// shape 0 Max HP, and a defaulted <see cref="AspdCoef"/> of 0 gives it an attack every never.
/// </para>
/// <para>
/// ⚠️ <see cref="UnitsPerDraw"/> lives on the row rather than on the pool, because it is a property
/// of the shape: `05` §6.1 heads the row <em>"<c>SWARM</c> (×3 units)"</em> and §6.4 restates it
/// from the pool's side — <em>"a <c>SWARM</c> draw spawns its 3 units"</em>. One draw, three bodies,
/// wherever the draw came from.
/// </para>
/// </remarks>
/// <param name="Id">Which of `05` §6.1's eight shapes this is.</param>
/// <param name="HpCoef">`05` §6 — <c>MaxHP = power × hpPerPower × hpCoef</c>.</param>
/// <param name="AtkCoef">`05` §6 — <c>ATK = power × atkPerPower × atkCoef</c>.</param>
/// <param name="DefCoef">`05` §6 — <c>DEF = power × defPerPower × defCoef</c>.</param>
/// <param name="AspdCoef">`05` §6 — <c>ASPD = baseAspd × aspdCoef</c>.</param>
/// <param name="Crit">`05` §6.1 — Crit Chance, taken as the stat.</param>
/// <param name="CritDamage">`05` §6.1 — Crit Damage bonus, taken as the stat.</param>
/// <param name="Dodge">`05` §6.1 — Dodge, taken as the stat.</param>
/// <param name="Lifesteal">`05` §6.1 — Lifesteal, taken as the stat.</param>
/// <param name="UnitsPerDraw">`05` §6.1/§6.4 — bodies spawned by one draw of this shape.</param>
/// <param name="OnHit">`05` §6.1a — which on-hit parameter set this shape carries.</param>
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
        $"×{UnitsPerDraw.ToString(CultureInfo.InvariantCulture)} units, onHit {OnHit}";

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
