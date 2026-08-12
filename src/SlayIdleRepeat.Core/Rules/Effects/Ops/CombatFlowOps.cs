using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// 🔒 Ten of `18` §2.4's eleven combat-flow ops. The eleventh, <c>STAT_COPY</c>, is
/// <see cref="StatCopyOp"/> — it is the one op in the DSL whose <c>target</c> does not name who it
/// writes to, and that inversion is worth a file of its own.
/// </summary>
/// <remarks>
/// <para>
/// These ops write <b>actor state</b> rather than HP: charge counters, cooldowns, targeting weight,
/// an armed death save. None of them has a number that means anything to `05` §4's damage pipeline,
/// which is why they route through <see cref="ICombatFlowSink"/> and not
/// <see cref="IAttackPipeline"/>.
/// </para>
/// <para>
/// 🔒 <b>Three of them carry a count, and `18` §2.4 named a key for exactly one.</b>
/// <c>EXTRA_ATTACK</c>'s count is its <c>value</c> (§7.3's <c>PK_FLURRY</c> is
/// <c>{"op":"EXTRA_ATTACK","value":1}</c>), but <c>ATTACK_MULT_NEXT</c> and <c>FORCE_CRIT_NEXT</c>
/// both say <em>"the next N attacks"</em> while `05` §4 has already spent their <c>value</c> on the
/// multiplier (<em>"<c>PK_OPENER</c>'s ×3 first attack"</em>). <c>charges</c> is the key added for
/// that under `18` §10 — see <see cref="EffectDefinition.Charges"/>.
/// </para>
/// </remarks>
internal static class CombatFlowOps
{
    /// <summary>
    /// `18` §2.4 / §7.3 — <c>EXTRA_ATTACK</c>: <em>"perform an additional attack immediately"</em>,
    /// <c>value</c> times.
    /// </summary>
    /// <remarks>
    /// The count is <c>value</c> and not <c>charges</c>: §7.3 authors <c>PK_FLURRY</c> as
    /// <c>{"op": "EXTRA_ATTACK", "value": 1, …}</c>, so the key already exists and adding a second
    /// spelling would be two ways to say one thing.
    /// </remarks>
    internal static double ExtraAttack(EffectDefinition effect, EffectOpContext context)
    {
        var attacks = WholeCount(effect, context.Seams.Values.ScaledValue(effect), "extra attacks");
        var attacker = OpTargets.Holder(context);

        foreach (var target in OpTargets.Resolve(effect, context))
        {
            context.Seams.Flow.ExtraAttack(attacker, target, attacks, effect.Id);
        }

        return attacks;
    }

    /// <summary>
    /// 🔒 `18` §2.4 — <c>ATTACK_MULT_NEXT</c>: multiply the damage of the next <c>charges</c>
    /// attacks by <c>value</c>.
    /// </summary>
    /// <remarks>
    /// Holder-scoped: <em>"the next N attacks"</em> are the holder's, which is why `05` §3.1 grants
    /// <c>PK_OPENER</c>'s charge at the pre-tick with no target in sight. The effect id travels
    /// because `05` §4 consumes the charges <em>"in ascending effect-id order"</em>.
    /// </remarks>
    internal static double AttackMultiplierCharges(EffectDefinition effect, EffectOpContext context)
    {
        var multiplier = OpRounding.Round(
            context.Seams.Values.ScaledValue(effect), effect.Id, "attack multiplier");

        context.Seams.Flow.GrantAttackMultiplierCharges(
            OpTargets.Holder(context), multiplier, RequireCharges(effect), effect.Id);

        return multiplier;
    }

    /// <summary>
    /// 🔒 `18` §2.4 — <c>FORCE_CRIT_NEXT</c>: the next <c>charges</c> attacks always crit.
    /// </summary>
    /// <remarks>
    /// ⚠️ Carries <b>no</b> <c>value</c> at all — the crit's size is the actor's own <c>CDMG</c>
    /// (`05` §4 step 4). An effect that authors one is refused rather than ignored: a forced crit
    /// written <c>{"charges": 2, "value": 3}</c> reads as "three attacks" to whoever wrote it, and
    /// silently dropping the 3 would ship that misreading.
    /// </remarks>
    internal static double ForcedCritCharges(EffectDefinition effect, EffectOpContext context)
    {
        if (effect.Value is not null)
        {
            throw new EffectContextException(
                effect.Id,
                "FORCE_CRIT_NEXT carries a value",
                "18 §2.4 gives it none — 'the next N attacks always crit', and how hard they crit is " +
                "the actor's own CDMG (05 §4 step 4). The count is 'charges'. Ignoring the value " +
                "would ship whichever misreading put it there.");
        }

        var charges = RequireCharges(effect);
        context.Seams.Flow.GrantForcedCritCharges(OpTargets.Holder(context), charges, effect.Id);

        return charges;
    }

    /// <summary>
    /// `18` §2.4 — <c>REDUCE_COOLDOWN</c>: <em>"reduce pet/boss ability cooldowns"</em>.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The value is a fraction of the cooldown, and `18` §2.4 does not say so.</b> `09` §4's
    /// <em>Relentless</em> — <em>"−3% per rank to all pet ability cooldowns"</em> — is the only
    /// authored user in any document, and it is a percentage. Reading it as seconds would make that
    /// talent remove 0.03 s from a 12 s cooldown. Recorded as errata against §2.4; no <c>valueMode</c>
    /// is added, because one authored user pointing one way is not two readings to choose between.
    /// </remarks>
    internal static double ReduceCooldown(EffectDefinition effect, EffectOpContext context)
    {
        var fraction = OpRounding.Round(
            context.Seams.Values.ScaledValue(effect), effect.Id, "cooldown reduction fraction");

        foreach (var target in OpTargets.Resolve(effect, context))
        {
            context.Seams.Flow.ReduceCooldowns(target, fraction, effect.Id);
        }

        return fraction;
    }

    /// <summary>
    /// 🔒 `18` §2.4 / R7 — <c>SURVIVE_LETHAL</c>: arm a save at the HP the effect names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// R7: §2.4 words it <em>"at a given HP fraction"</em>, §7.4 writes <c>"value": 1</c> — which as
    /// a fraction is <b>full HP</b> — and `06` words the same perk <em>"survive a lethal hit at 1
    /// HP"</em>. The <c>valueMode</c> key added under `18` §10 is what lets both be authored:
    /// <c>PK_UNBREAKABLE</c> is <c>{"value": 1, "valueMode": "FLAT"}</c> = 1 HP, and an effect that
    /// really means half health writes <c>{"value": 0.5}</c> on the <c>SELF_MAXHP_PCT</c> default.
    /// </para>
    /// <para>
    /// 🔒 Holder-scoped and <b>not</b> an <c>ON_REVIVE</c>: `18` §3 is explicit that
    /// <em>"<c>SURVIVE_LETHAL</c> does not count (the actor never died)"</em>. The seam has two
    /// members for exactly that reason.
    /// </para>
    /// </remarks>
    internal static double SurviveLethal(EffectDefinition effect, EffectOpContext context)
    {
        var holder = OpTargets.Holder(context);
        var hp = SurvivalHp(effect, context, OpValueRules.SurviveLethal);

        context.Seams.Flow.ArmSurviveLethal(holder, hp, effect.Id);

        return hp;
    }

    /// <summary>`18` §2.4 — <c>REVIVE</c>: return from 0 HP at a fraction of Max HP. Fires <c>ON_REVIVE</c>.</summary>
    internal static double Revive(EffectDefinition effect, EffectOpContext context)
    {
        var holder = OpTargets.Holder(context);
        var hp = SurvivalHp(effect, context, OpValueRules.Revive);

        context.Seams.Flow.ArmRevive(holder, hp, effect.Id);

        return hp;
    }

    /// <summary>`18` §2.4 / §7.8 — <c>SUMMON</c>: <c>value</c> of <c>archetype</c>, at most <c>maxAlive</c>.</summary>
    internal static double Summon(EffectDefinition effect, EffectOpContext context)
    {
        var archetype = effect.Archetype ?? throw new EffectContextException(
            effect.Id,
            "SUMMON names no archetype",
            "18 §2.4 spawns 'N enemies of an archetype' and §7.8 writes it — " +
            "{\"op\":\"SUMMON\",\"archetype\":\"SWARM\",\"value\":2}. There is no default archetype.");

        var count = WholeCount(effect, context.Seams.Values.ScaledValue(effect), "summons");

        context.Seams.Flow.Summon(
            OpTargets.Holder(context), archetype, count, effect.MaxAlive, effect.Id);

        return count;
    }

    /// <summary>
    /// `18` §2.4 — <c>CLEAR_SUMMONS</c>: <em>"despawn all living summons owned by the target
    /// (default <c>SELF</c>)"</em>.
    /// </summary>
    /// <remarks>
    /// 🔒 <em>"Despawned ≠ killed: no <c>ON_DEATH</c>, no <c>ON_KILL</c>, no on-death explosions, no
    /// rewards"</em> — which is why the seam has its own member rather than the op looping and
    /// dealing lethal damage. Ossuary King's <em>Rise Again</em> would otherwise detonate its own
    /// Volatile adds.
    /// </remarks>
    internal static double ClearSummons(EffectDefinition effect, EffectOpContext context)
    {
        foreach (var owner in OpTargets.OrSelf(effect, context))
        {
            context.Seams.Flow.ClearSummons(owner, effect.Id);
        }

        return 0.0;
    }

    /// <summary>
    /// `18` §2.4 — <c>SET_TARGET_PRIORITY</c>. `05` §3.2: default <c>0</c>, <c>-1</c>
    /// deprioritises (Sporequeen's sporelings), <c>+1</c> forces focus.
    /// </summary>
    internal static double SetTargetPriority(EffectDefinition effect, EffectOpContext context)
    {
        var priority = OpRounding.Round(
            context.Seams.Values.ScaledValue(effect), effect.Id, "target priority");

        foreach (var target in OpTargets.Resolve(effect, context))
        {
            context.Seams.Flow.SetTargetPriority(target, priority, effect.Id);
        }

        return priority;
    }

    /// <summary>
    /// 🔒 `18` §2.4 / §7.10 — <c>DAMAGE_TAKEN_MULT</c>: multiply incoming damage.
    /// <c>PK_STALWART</c>'s 0.80, Rimehold's Core's 1.6.
    /// </summary>
    /// <remarks>
    /// `05` §4 step 6 takes the <b>product</b> of every active one in ascending effect-id order, so
    /// this accumulates rather than replaces — <see cref="ICombatFlowSink.AddDamageTakenMultiplier"/>
    /// is named for that. A negative multiplier is refused: `05` §4 step 6 multiplies the damage by
    /// it, so a negative one would heal the defender through the damage pipeline.
    /// </remarks>
    internal static double DamageTakenMultiplier(EffectDefinition effect, EffectOpContext context)
    {
        var multiplier = OpRounding.Round(
            context.Seams.Values.ScaledValue(effect), effect.Id, "damage-taken multiplier");

        if (multiplier <= 0.0)
        {
            throw new EffectContextException(
                effect.Id,
                $"DAMAGE_TAKEN_MULT is {multiplier.ToString("R", CultureInfo.InvariantCulture)}",
                "05 §4 step 6 multiplies the incoming hit by it: a negative multiplier turns every " +
                "hit into a heal and ZERO is permanent invulnerability for the duration. No document " +
                "authorises either — 18 §7.10's PK_STALWART is 0.80 and 17 §6's Rimehold's Core is " +
                "1.6. ⚠️ Zero is refused rather than admitted because it is reachable by accident: " +
                "a valueScale whose step count comes out 0 yields it (18 §1.1), and the alternative " +
                "is a build that cannot be damaged with nothing going red.");
        }

        foreach (var target in OpTargets.Resolve(effect, context))
        {
            context.Seams.Flow.AddDamageTakenMultiplier(target, multiplier, effect.Duration, effect.Id);
        }

        return multiplier;
    }

    /// <summary>The HP a death save leaves the actor at, under the op's own value-mode rules.</summary>
    private static double SurvivalHp(
        EffectDefinition effect, EffectOpContext context, OpValueRules rules)
    {
        var holder = OpTargets.Holder(context);
        var hp = OpValue.Amount(effect, context, holder, rules);

        return hp > 0.0
            ? hp
            : throw new EffectContextException(
                effect.Id,
                $"{effect.Op} would leave the actor at {hp.ToString("R", CultureInfo.InvariantCulture)} HP",
                "18 §2.4 survives or returns AT an HP value, and 0 is the state both ops exist to " +
                "prevent — an actor left there dies again on the same tick, and the save reads as " +
                "having fired.");
    }

    /// <summary>`18` §2.4's <c>charges</c> — the N of "the next N attacks".</summary>
    private static int RequireCharges(EffectDefinition effect) =>
        effect.Charges is { } charges && charges >= 1
            ? charges
            : throw new EffectContextException(
                effect.Id,
                effect.Charges is null
                    ? $"{effect.Op} names no charges"
                    : $"{effect.Op} names {effect.Charges.Value.ToString(CultureInfo.InvariantCulture)} charges",
                "18 §2.4 covers 'the next N attacks', and M2-03 added the 'charges' key for that N " +
                "under 18 §10 because the one 'value' is already the multiplier (05 §4). N is a " +
                "whole number of attacks, so at least one — an effect granting none is authored dead " +
                "weight that reads as a live buff.");

    /// <summary>A count of things, which the DSL carries as a <see cref="double"/>.</summary>
    private static int WholeCount(EffectDefinition effect, double value, string what)
    {
        var rounded = OpRounding.Round(value, effect.Id, what);

        if (rounded < 1.0 || rounded != Math.Floor(rounded) || rounded > int.MaxValue)
        {
            throw new EffectContextException(
                effect.Id,
                $"{effect.Op} counts {what} as {rounded.ToString("R", CultureInfo.InvariantCulture)}",
                "18 §2.4 counts whole actors and whole attacks — §7.3's PK_FLURRY is value 1 and " +
                "§7.8's Thornmaw is value 2. Truncating a fraction would make a valueScale that " +
                "reached 1.9 spawn one add, and 0 is an effect that fires and does nothing.");
        }

        return (int)rounded;
    }
}
