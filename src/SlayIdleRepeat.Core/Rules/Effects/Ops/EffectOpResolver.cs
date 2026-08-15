using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Values;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>How one op resolution ended.</summary>
/// <param name="Op">Which op ran.</param>
/// <param name="Amount">
/// The op's own number, rounded to 4 dp. <c>0</c> for ops with no magnitude and for every queued op.
/// <para>
/// For five of the seven damage/heal ops this is what the op asked for, not what actually happened —
/// <c>HEAL</c>/<c>HEAL_LEECH</c> are clipped by overheal, <c>SHIELD</c> by the pool cap, and
/// <c>DAMAGE_TRUE</c>/<c>DAMAGE_MAXHP_PCT</c> still meet DR%, wards and remaining HP downstream.
/// <c>DAMAGE</c> is the exception: it reports the real HP lost via
/// <see cref="IAttackPipeline.ResolveAttack"/>.
/// </para>
/// </param>
/// <param name="Disposition">Whether the simulator resolved it or handed it to the run controller.</param>
/// <param name="Basis">
/// <c>DAMAGE</c> only — the pre-absorption hit, summed over the op's targets. <c>0</c> for every other op.
/// </param>
/// <remarks>
/// <see cref="Amount"/> is not the number to feed <c>EffectOpContext.DamageDealt</c>: for
/// <c>DAMAGE</c> that's the post-absorption HP lost, but lifesteal and thorns must read the
/// pre-absorption figure (<see cref="Basis"/>) — a lifesteal attacker still heals off a fully-warded
/// hit. The two fields exist so the wiring can't be a guess.
/// </remarks>
internal readonly record struct EffectOpOutcome(
    EffectOp Op, double Amount, OpDisposition Disposition, double Basis = 0.0);

/// <summary>What the simulator did with an op.</summary>
internal enum OpDisposition
{
    /// <summary>Resolved here and now, against the seams.</summary>
    RESOLVED = 1,

    /// <summary>Appended to the run queue and not resolved — the run controller applies it when the battle resolves.</summary>
    QUEUED_FOR_RUN = 2,

    /// <summary><c>STAT_CONVERT</c> / <c>STAT_CAP_OVERRIDE</c>, applied by aggregation only, never by a firing effect.</summary>
    AGGREGATED = 3,
}

/// <summary>The one place all 44 ops are routed, and the only thing in the DSL that knows which op does what.</summary>
/// <remarks>
/// <para>
/// Everything a perk, talent, affix, aura, curse or boss mechanic can do is one arm of the switch
/// below — adding a mechanic means adding an op, not a branch on an id. The switch is total, and its
/// default arm throws rather than being a silent safety net.
/// </para>
/// <para>
/// Two ops route nowhere by design: <c>STAT_CONVERT</c> and <c>STAT_CAP_OVERRIDE</c> are applied by
/// aggregation only, since their arithmetic needs a post-aggregation value neither a trigger nor this
/// resolver has in hand. The other four basic stat ops resolve through <c>ITriggeredStatSink</c> when
/// a trigger fires them. Every queued op is queued rather than resolved;
/// <see cref="OpDisposition"/> is how the caller tells those apart from "it did nothing".
/// </para>
/// <para>
/// What this resolver does not do: evaluate the effect's condition, decide whether the trigger
/// fired, apply duration or stacking, or order effects. It's handed an effect already decided to fire.
/// </para>
/// </remarks>
internal static class EffectOpResolver
{
    /// <summary>Resolves one op.</summary>
    /// <param name="effect">The effect, already gated and triggered.</param>
    /// <param name="context">The firing context and the seams.</param>
    /// <exception cref="EffectContextException">
    /// The effect is malformed for its op, a value mode has no basis, a target token's subject is
    /// absent, or a seam is unwired.
    /// </exception>
    internal static EffectOpOutcome Resolve(EffectDefinition effect, EffectOpContext context)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(context);

        return effect.Op switch
        {
            // The four basic stat ops, applied by aggregation when untriggered (ALWAYS), and
            // through ITriggeredStatSink when a trigger fires them. An effect always takes exactly
            // one of the two paths, never both, since an untriggered effect never reaches this
            // resolver at all.
            EffectOp.STAT_ADD_FLAT or
            EffectOp.STAT_ADD_PCT or
            EffectOp.STAT_MULT or
            EffectOp.STAT_SET => FiredStat(effect, context),

            // STAT_CONVERT / STAT_CAP_OVERRIDE: applied by aggregation only, never by a firing
            // effect — their arithmetic needs a post-aggregation value only StatAggregation has.
            EffectOp.STAT_CONVERT or
            EffectOp.STAT_CAP_OVERRIDE => Aggregated(effect),

            // Damage and healing (7).
            EffectOp.DAMAGE => Damaged(effect, DamageAndHealingOps.Damage(effect, context)),
            EffectOp.DAMAGE_TRUE => Resolved(effect, DamageAndHealingOps.DamageTrue(effect, context)),
            EffectOp.DAMAGE_MAXHP_PCT => Resolved(effect, DamageAndHealingOps.DamageMaxHpPct(effect, context)),
            EffectOp.HEAL => Resolved(effect, DamageAndHealingOps.Heal(effect, context)),
            EffectOp.HEAL_LEECH => Resolved(effect, DamageAndHealingOps.HealLeech(effect, context)),
            EffectOp.SHIELD => Resolved(effect, DamageAndHealingOps.Shield(effect, context)),
            EffectOp.REFLECT => Resolved(effect, DamageAndHealingOps.Reflect(effect, context)),

            // Status (6).
            EffectOp.APPLY_STATUS => Resolved(effect, StatusOps.Apply(effect, context)),
            EffectOp.REMOVE_STATUS => Resolved(effect, StatusOps.Remove(effect, context)),
            EffectOp.EXTEND_STATUS => Resolved(effect, StatusOps.Extend(effect, context)),
            EffectOp.IMMUNE_STATUS => Resolved(effect, StatusOps.GrantImmunity(effect, context)),
            EffectOp.STATUS_POWER_PCT => Resolved(effect, StatusOps.ScaleOutgoingPower(effect, context)),
            EffectOp.STATUS_DURATION_PCT => Resolved(effect, StatusOps.ScaleIncomingDuration(effect, context)),

            // Combat-flow (12).
            EffectOp.EXTRA_ATTACK => Resolved(effect, CombatFlowOps.ExtraAttack(effect, context)),
            EffectOp.ATTACK_MULT_NEXT => Resolved(effect, CombatFlowOps.AttackMultiplierCharges(effect, context)),
            EffectOp.FORCE_CRIT_NEXT => Resolved(effect, CombatFlowOps.ForcedCritCharges(effect, context)),
            EffectOp.REDUCE_COOLDOWN => Resolved(effect, CombatFlowOps.ReduceCooldown(effect, context)),
            EffectOp.SURVIVE_LETHAL => Resolved(effect, CombatFlowOps.SurviveLethal(effect, context)),
            EffectOp.REVIVE => Resolved(effect, CombatFlowOps.Revive(effect, context)),
            EffectOp.SUMMON => Resolved(effect, CombatFlowOps.Summon(effect, context)),
            EffectOp.SET_TARGET_PRIORITY => Resolved(effect, CombatFlowOps.SetTargetPriority(effect, context)),
            EffectOp.DAMAGE_TAKEN_MULT => Resolved(effect, CombatFlowOps.DamageTakenMultiplier(effect, context)),
            EffectOp.CLEAR_SUMMONS => Resolved(effect, CombatFlowOps.ClearSummons(effect, context)),
            EffectOp.STAT_COPY => Resolved(effect, StatCopyOp.Resolve(effect, context)),
            EffectOp.RANDOM_OUTCOME => Resolved(effect, CombatFlowOps.RandomOutcome(effect, context)),

            // Run and board (13), queued, never resolved.
            EffectOp.GRANT_CURRENCY or
            EffectOp.GRANT_ITEM or
            EffectOp.GRANT_PERK or
            EffectOp.UPGRADE_PERK or
            EffectOp.MODIFY_DIE_FACE or
            EffectOp.GRANT_REROLL or
            EffectOp.MOVE_NODES or
            EffectOp.REVEAL_TILES or
            EffectOp.RESOLVE_TILE_AGAIN or
            EffectOp.MODIFY_SHOP or
            EffectOp.MODIFY_DROP_TABLE or
            EffectOp.APPLY_CURSE or
            EffectOp.CLEANSE_CURSE => new EffectOpOutcome(
                effect.Op, RunBoardOps.Queue(effect, context), OpDisposition.QUEUED_FOR_RUN),

            _ => throw new EffectContextException(
                effect.Id,
                $"op {effect.Op} is not one of 18 §2's 44",
                "18 §11 fixes the count and 18 §10 is the procedure for a forty-fifth: the op, its " +
                "schema branch and its row in 18, in one commit — and an arm here."),
        };
    }

    /// <summary>A basic stat op reached through a trigger firing it, rather than aggregation collecting it as an untriggered passive.</summary>
    /// <remarks>
    /// The value is read and rounded exactly like any other firing op. What's new is where the
    /// number goes: into <see cref="ITriggeredStatSink"/>, held against the effect's own duration
    /// until the next aggregation pass folds it in.
    /// <para>
    /// Resolved through targets, not just the holder — a target token can name a set the holder
    /// isn't a member of (e.g. a boss effect that debuffs the hero's ASPD).
    /// </para>
    /// </remarks>
    private static EffectOpOutcome FiredStat(EffectDefinition effect, EffectOpContext context)
    {
        var stat = StatOps.SingleStatOf(effect);
        var value = OpRounding.Round(
            ValueScaleEvaluator.EffectiveValue(effect, context.Evaluation), effect.Id, "stat op value");
        var targets = OpTargets.Resolve(effect, context);

        context.Seams.TriggeredStats.Apply(
            targets, effect.Op, stat, value, effect.Duration, effect.Stacking, effect.Id);

        return new EffectOpOutcome(effect.Op, value, OpDisposition.RESOLVED);
    }

    private static EffectOpOutcome Resolved(EffectDefinition effect, double amount) =>
        new(effect.Op, amount, OpDisposition.RESOLVED);

    private static EffectOpOutcome Damaged(EffectDefinition effect, DamageAndHealingOps.DamageTotals totals) =>
        new(effect.Op, totals.HpLost, OpDisposition.RESOLVED, totals.Basis);

    private static EffectOpOutcome Aggregated(EffectDefinition effect) =>
        new(effect.Op, 0.0, OpDisposition.AGGREGATED);
}
