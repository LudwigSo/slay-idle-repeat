using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// 🔒 `18` §2.2's seven damage and healing ops, routed by `05` §4.2's table.
/// </summary>
/// <remarks>
/// <para>
/// `05` §4.2 is the whole of this class's design, and it is a table of <b>routes</b>, not of
/// formulas — which is why every method below ends in one call on <see cref="IAttackPipeline"/>:
/// </para>
/// <list type="table">
///   <listheader><term>Op</term><description>Route</description></listheader>
///   <item><term><c>DAMAGE</c></term><description>the <b>full</b> <c>ResolveAttack</c> pipeline —
///   dodge, mitigation, crit, block, DR, floor, wards. <c>AttackMultiplier</c> = the op's
///   value.</description></item>
///   <item><term><c>DAMAGE_TRUE</c></term><description><b>bypasses everything</b>: no dodge,
///   mitigation, crit, block, DR, <c>DAMAGE_TAKEN_MULT</c>, floor or wards. Phase check still runs;
///   no lifesteal or thorns.</description></item>
///   <item><term><c>DAMAGE_MAXHP_PCT</c></term><description>no dodge, crit, block, mitigation or
///   floor; <c>DR%</c> and <c>DAMAGE_TAKEN_MULT</c> <b>do</b> apply; wards absorb; no lifesteal or
///   thorns.</description></item>
///   <item><term><c>HEAL</c> / <c>HEAL_LEECH</c></term><description>through <c>Heal()</c> (`05`
///   §4.3) — <c>HEAL%</c> applies.</description></item>
///   <item><term><c>SHIELD</c></term><description>a ward grant (`05` §4.1).</description></item>
///   <item><term><c>REFLECT</c></term><description>adds to <c>THORN</c> for its
///   duration.</description></item>
/// </list>
/// <para>
/// 🔒 <b>R4 — <c>REFLECT</c> is not a contradiction.</b> `18` §2.2 gives its <em>meaning</em>
/// (<em>"return a % of incoming damage"</em>) and `05` §4.2 its <em>implementation</em> (<em>"adds
/// to <c>THORN</c> for its duration"</em>). One rule, stated twice.
/// </para>
/// <para>
/// ⚠️ <b>Every one of the seven multiplies its own number and then stops.</b> Nothing here knows
/// what mitigation is, and nothing here decides whether a ward absorbs — those are `05` §4's and
/// M2-09's. The one exception is <see cref="EffectTagging.IsSelfInflictedCost"/>, which the op
/// reads and <em>reports</em> so that `05` §4.1's bypass list (b) is keyed off one reading of
/// `18` §7.5's <c>drawback</c> tag rather than two.
/// </para>
/// </remarks>
internal static class DamageAndHealingOps
{
    /// <summary>What one <c>DAMAGE</c> op's resolved attacks came to, in `05` §4's two currencies.</summary>
    /// <param name="HpLost">Step 9 — what came off HP after ward absorption.</param>
    /// <param name="Basis">
    /// 🔒 Step 8 — the post-mitigation, post-floor hit <b>before</b> absorption. The number
    /// lifesteal and thorns read, and therefore the number a <c>DAMAGE_DEALT_PCT</c> value mode
    /// needs. The two are equal only on an unwarded target.
    /// </param>
    internal readonly record struct DamageTotals(double HpLost, double Basis);

    /// <summary>
    /// 🔒 `05` §4.2 — <c>DAMAGE</c> through the full attack pipeline, once per resolved target.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The value is passed through unmultiplied.</b> `05` §4 step 2 is
    /// <c>raw = attacker.ATK × attacker.AttackMultiplier × (1 + DMG%)</c> and `05` §4.2 sets
    /// <c>AttackMultiplier</c> to <em>the op's value</em>. Multiplying by ATK here as well —
    /// which is what `18` §2.2's blanket <em>"a multiple of the source's ATK"</em> reads like out of
    /// context — would square the attacker's attack power. <c>PK_CLEAVE</c> at <c>value: 0.40</c> is
    /// a ×0.4 attack, not 0.4 × ATK of flat damage.
    /// </remarks>
    internal static DamageTotals Damage(EffectDefinition effect, EffectOpContext context)
    {
        var attacker = OpTargets.Holder(context);

        // Validated, not applied: see OpValueRules.Damage for why Amount() is not used here.
        OpValue.ModeOf(effect, OpValueRules.Damage);

        var multiplier = OpRounding.Round(
            context.Seams.Values.ScaledValue(effect), effect.Id, "AttackMultiplier");

        var lost = 0.0;
        var basis = 0.0;

        foreach (var target in OpTargets.Resolve(effect, context))
        {
            var resolution = context.Seams.Attack.ResolveAttack(attacker, target, multiplier, effect.Id);

            lost += resolution.HpLost;
            basis += resolution.Basis;
        }

        return new DamageTotals(
            OpRounding.Round(lost, effect.Id, "damage dealt"),
            OpRounding.Round(basis, effect.Id, "on-damage basis"));
    }

    /// <summary>🔒 `05` §4.2 — <c>DAMAGE_TRUE</c>, straight onto HP.</summary>
    internal static double DamageTrue(EffectDefinition effect, EffectOpContext context)
    {
        var total = 0.0;
        foreach (var target in OpTargets.Resolve(effect, context))
        {
            var amount = OpValue.Amount(effect, context, target, OpValueRules.DamageTrue);
            context.Seams.Attack.DealTrueDamage(target, amount, effect.Id);
            total += amount;
        }

        return OpRounding.Round(total, effect.Id, "true damage");
    }

    /// <summary>
    /// 🔒 `05` §4.2 — <c>DAMAGE_MAXHP_PCT</c>: mitigation-free, but <c>DR%</c> and
    /// <c>DAMAGE_TAKEN_MULT</c> apply and wards absorb — unless `05` §4.1's bypass class (b) does.
    /// </summary>
    internal static double DamageMaxHpPct(EffectDefinition effect, EffectOpContext context)
    {
        // 🔒 Read ONCE, here, and reported to M2-09. See EffectTagging.IsSelfInflictedCost.
        var selfInflicted = EffectTagging.IsSelfInflictedCost(effect);

        var total = 0.0;
        foreach (var target in OpTargets.Resolve(effect, context))
        {
            var amount = OpValue.Amount(effect, context, target, OpValueRules.DamageMaxHpPct);
            context.Seams.Attack.DealMaxHpPctDamage(target, amount, selfInflicted, effect.Id);
            total += amount;
        }

        return OpRounding.Round(total, effect.Id, "max-HP-percent damage");
    }

    /// <summary>`05` §4.2 — <c>HEAL</c> through <c>Heal()</c>.</summary>
    internal static double Heal(EffectDefinition effect, EffectOpContext context) =>
        HealEach(effect, context, OpValueRules.Heal);

    /// <summary>
    /// `05` §4.2 — <c>HEAL_LEECH</c> through the same <c>Heal()</c>, off `05` §4 step 8's
    /// pre-absorption basis.
    /// </summary>
    internal static double HealLeech(EffectDefinition effect, EffectOpContext context) =>
        HealEach(effect, context, OpValueRules.HealLeech);

    /// <summary>🔒 `05` §4.1 / §4.2 — <c>SHIELD</c>, one ward segment per resolved target.</summary>
    /// <remarks>
    /// <c>sourceCapPct</c> travels with the grant rather than being applied here: `18` §2.2 caps
    /// <em>"the total unbroken ward contributed by that effect <b>instance</b>"</em>, which is a
    /// running total across grants that only the ward pool holds. A cap applied per grant would let
    /// <c>PK_TRANSFUSION</c> stack four 20% segments.
    /// </remarks>
    internal static double Shield(EffectDefinition effect, EffectOpContext context)
    {
        var total = 0.0;
        foreach (var target in OpTargets.Resolve(effect, context))
        {
            var amount = OpValue.Amount(effect, context, target, OpValueRules.Shield);
            context.Seams.Attack.GrantWard(target, amount, effect.SourceCapPct, effect.Id);
            total += amount;
        }

        return OpRounding.Round(total, effect.Id, "ward granted");
    }

    /// <summary>🔒 `05` §4.2 / R4 — <c>REFLECT</c> adds to <c>THORN</c> for the effect's duration.</summary>
    internal static double Reflect(EffectDefinition effect, EffectOpContext context)
    {
        var total = 0.0;
        foreach (var target in OpTargets.Resolve(effect, context))
        {
            var fraction = OpValue.Amount(effect, context, target, OpValueRules.Reflect);
            context.Seams.Attack.AddThorns(target, fraction, effect.Duration, effect.Id);
            total += fraction;
        }

        return OpRounding.Round(total, effect.Id, "thorns granted");
    }

    private static double HealEach(EffectDefinition effect, EffectOpContext context, OpValueRules rules)
    {
        var total = 0.0;
        foreach (var target in OpTargets.Resolve(effect, context))
        {
            var amount = OpValue.Amount(effect, context, target, rules);
            context.Seams.Attack.Heal(target, amount, effect.Id);
            total += amount;
        }

        return OpRounding.Round(total, effect.Id, "healing");
    }
}
