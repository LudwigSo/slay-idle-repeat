using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>The seven damage and healing ops, routed by a fixed table.</summary>
/// <remarks>
/// <para>
/// A table of routes, not of formulas — every method below ends in one call on
/// <see cref="IAttackPipeline"/>:
/// </para>
/// <list type="table">
///   <listheader><term>Op</term><description>Route</description></listheader>
///   <item><term><c>DAMAGE</c></term><description>the full <c>ResolveAttack</c> pipeline —
///   dodge, mitigation, crit, block, DR, floor, wards. <c>AttackMultiplier</c> = the op's
///   value.</description></item>
///   <item><term><c>DAMAGE_TRUE</c></term><description>bypasses everything: no dodge,
///   mitigation, crit, block, DR, <c>DAMAGE_TAKEN_MULT</c>, floor or wards. Phase check still runs;
///   no lifesteal or thorns.</description></item>
///   <item><term><c>DAMAGE_MAXHP_PCT</c></term><description>no dodge, crit, block, mitigation or
///   floor; <c>DR%</c> and <c>DAMAGE_TAKEN_MULT</c> do apply; wards absorb; no lifesteal or
///   thorns.</description></item>
///   <item><term><c>HEAL</c> / <c>HEAL_LEECH</c></term><description>through <c>Heal()</c> —
///   <c>HEAL%</c> applies.</description></item>
///   <item><term><c>SHIELD</c></term><description>a ward grant.</description></item>
///   <item><term><c>REFLECT</c></term><description>adds to <c>THORN</c> for its
///   duration.</description></item>
/// </list>
/// <para>
/// Every one of the seven multiplies its own number and then stops — nothing here knows what
/// mitigation is or decides whether a ward absorbs. The one exception is
/// <see cref="EffectTagging.IsSelfInflictedCost"/>, which the op reads and reports so the
/// ward-bypass list is keyed off one reading of the <c>drawback</c> tag rather than two.
/// </para>
/// </remarks>
internal static class DamageAndHealingOps
{
    /// <summary>What one <c>DAMAGE</c> op's resolved attacks came to.</summary>
    /// <param name="HpLost">What came off HP after ward absorption.</param>
    /// <param name="Basis">
    /// The post-mitigation, post-floor hit before absorption — the number lifesteal, thorns and a
    /// <c>DAMAGE_DEALT_PCT</c> value mode read. Equal to <see cref="HpLost"/> only on an unwarded target.
    /// </param>
    internal readonly record struct DamageTotals(double HpLost, double Basis);

    /// <summary><c>DAMAGE</c> through the full attack pipeline, once per resolved target.</summary>
    /// <remarks>
    /// The value is passed through unmultiplied — the pipeline already sets
    /// <c>AttackMultiplier</c> to the op's value, so multiplying by ATK here too would square the
    /// attacker's attack power. <c>PK_CLEAVE</c> at <c>value: 0.40</c> is a ×0.4 attack, not 0.4 × ATK
    /// of flat damage.
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

    /// <summary><c>DAMAGE_TRUE</c>, straight onto HP.</summary>
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
    /// <c>DAMAGE_MAXHP_PCT</c>: mitigation-free, but <c>DR%</c> and <c>DAMAGE_TAKEN_MULT</c> apply
    /// and wards absorb — unless the ward-bypass rule applies.
    /// </summary>
    internal static double DamageMaxHpPct(EffectDefinition effect, EffectOpContext context)
    {
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

    /// <summary><c>HEAL</c> through <c>Heal()</c>.</summary>
    internal static double Heal(EffectDefinition effect, EffectOpContext context) =>
        HealEach(effect, context, OpValueRules.Heal);

    /// <summary><c>HEAL_LEECH</c> through the same <c>Heal()</c>, off the pre-absorption damage basis.</summary>
    internal static double HealLeech(EffectDefinition effect, EffectOpContext context) =>
        HealEach(effect, context, OpValueRules.HealLeech);

    /// <summary><c>SHIELD</c>, one ward segment per resolved target.</summary>
    /// <remarks>
    /// <c>sourceCapPct</c> travels with the grant rather than being applied here: the cap is on the
    /// total unbroken ward contributed by that effect instance, a running total only the ward pool
    /// holds. A cap applied per grant would let <c>PK_TRANSFUSION</c> stack four 20% segments.
    /// </remarks>
    internal static double Shield(EffectDefinition effect, EffectOpContext context)
    {
        // KNOWN GAP: effect.Duration is dropped here, so the ward is permanent for the fight.
        // GrantWard's signature carries no duration and several other call sites already depend on
        // it; an authored SHIELD with a duration silently grants a ward for the whole fight instead.
        // No shipped content authors one today; ShieldDurationExpiryTests catches the day one does.
        var total = 0.0;
        foreach (var target in OpTargets.Resolve(effect, context))
        {
            var amount = OpValue.Amount(effect, context, target, OpValueRules.Shield);
            context.Seams.Attack.GrantWard(target, amount, effect.SourceCapPct, effect.Id);
            total += amount;
        }

        return OpRounding.Round(total, effect.Id, "ward granted");
    }

    /// <summary><c>REFLECT</c> adds to <c>THORN</c> for the effect's duration.</summary>
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
