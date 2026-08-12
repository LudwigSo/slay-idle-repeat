using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>How one op resolution ended.</summary>
/// <param name="Op">Which of `18` §2's 44 ops ran.</param>
/// <param name="Amount">
/// The op's own number, rounded to 4 dp. <c>0</c> for the ops that carry no magnitude
/// (<c>REMOVE_STATUS</c>, <c>CLEAR_SUMMONS</c>) and for every queued §2.5 op.
/// <para>
/// 🔴 <b>For five of the seven §2.2 ops this is what the op ASKED FOR, not what happened.</b>
/// <c>HEAL</c> and <c>HEAL_LEECH</c> are clipped by `05` §4.3's
/// <c>min(amount × HEAL%, MaxHP − HP)</c>; <c>SHIELD</c> is clamped by the pool cap and by
/// <c>sourceCapPct</c> (`05` §4.1); <c>DAMAGE_TRUE</c> and <c>DAMAGE_MAXHP_PCT</c> still meet DR%,
/// <c>DAMAGE_TAKEN_MULT</c>, wards and the actor's remaining HP downstream. All five seam members
/// return <see langword="void"/>, so the engine — not the op — knows the outcome, and M2-09 emits
/// the `05` §7 events for them. <c>DAMAGE</c> is the exception: it routes through
/// <see cref="IAttackPipeline.ResolveAttack"/> and reports the real HP lost.
/// </para>
/// <para>
/// ⚠️ A caller writing this straight into a <c>CombatEvent</c>'s value slot would log a 500 heal on
/// a full-health actor. Read the engine's own number for the five.
/// </para>
/// </param>
/// <param name="Disposition">Whether the simulator resolved it or handed it to the run controller.</param>
/// <param name="Basis">
/// 🔴 <c>DAMAGE</c> only — `05` §4 step 8's <em>on-damage basis</em>: the post-mitigation,
/// post-floor hit <b>before</b> ward absorption, summed over the op's targets. <c>0</c> for every
/// other op.
/// </param>
/// <remarks>
/// 🔴 <b><see cref="Amount"/> is NOT the number to feed <c>EffectOpContext.DamageDealt</c>.</b> For
/// <c>DAMAGE</c> it is the HP actually lost (step 9, post-absorption), and `05` §4.1 is explicit
/// that lifesteal and thorns read the <em>pre</em>-absorption figure — <em>"a lifesteal attacker
/// still heals off a fully-warded hit"</em>. M2-04 wires the on-hit family and must pass
/// <see cref="Basis"/> there; passing <see cref="Amount"/> would make every leech heal nothing off
/// a shielded target, silently. The two fields exist so the wiring cannot be a guess.
/// </remarks>
internal readonly record struct EffectOpOutcome(
    EffectOp Op, double Amount, OpDisposition Disposition, double Basis = 0.0);

/// <summary>What the simulator did with an op.</summary>
internal enum OpDisposition
{
    /// <summary>Resolved here and now, against the seams.</summary>
    RESOLVED = 1,

    /// <summary>
    /// 🔒 `18` §2.5 — appended to the run queue and <b>not</b> resolved. Not a failure and not a
    /// no-op: the run controller applies it when the battle resolves.
    /// </summary>
    QUEUED_FOR_RUN = 2,

    /// <summary>
    /// `18` §8 step 6 / step 9 — a stat op the aggregation applies, not the simulator. Reaching one
    /// through this resolver is a routing mistake, so it is named rather than silently skipped.
    /// </summary>
    AGGREGATED = 3,
}

/// <summary>
/// 🔒 `18` §2 — the one place all 44 ops are routed, and the only thing in the DSL that knows which
/// op does what.
/// </summary>
/// <remarks>
/// <para>
/// `18`'s headnote is what this type exists to keep true: <em>"there is no per-perk, per-talent or
/// per-boss code"</em>. Everything a perk, talent, affix, aura, curse or boss mechanic can do is one
/// arm of the switch below, and adding a mechanic means adding an <c>op</c> — with its schema branch
/// and its row in `18`, in one commit (§10) — not a branch on an id.
/// </para>
/// <para>
/// 🔒 <b>The switch is total and its default arm cannot be the safety net.</b> C# requires a default
/// arm on an enum switch (<c>CS8524</c>), so a forty-fifth op would fall into it and throw in
/// whichever battle first authored one. <c>EffectOpResolverTests.Every_op_of_18_2_is_routed</c> is
/// what catches it at build time by enumerating <see cref="EffectOps.All"/> — the same arrangement
/// M2-01 records for <c>EffectOps.FamilyOf</c>.
/// </para>
/// <para>
/// ⚠️ <b>Three ops route nowhere, and all three are correct.</b> <c>STAT_CONVERT</c> and
/// <c>STAT_CAP_OVERRIDE</c> are applied by `18` §8's aggregation at steps 6 and 9, not by a firing
/// effect (<see cref="StatOps"/> holds their arithmetic, <c>Rules/Stats/StatOpBehaviour.cs</c> the
/// plumbing); the other four §2.1 ops are the same. And every §2.5 op is queued rather than
/// resolved. <see cref="OpDisposition"/> is how the caller tells those apart from "it did nothing".
/// </para>
/// <para>
/// ⚠️ <b>What this resolver does NOT do.</b> It does not evaluate the effect's condition (`18` §4 —
/// M2-05, called by M2-02's resolver), it does not decide whether the trigger fired (§3 — M2-04), it
/// does not apply duration or stacking (§6 — M2-06), and it does not order effects (§8 — M2-02
/// through <see cref="EffectOrder"/>). It is handed an effect that has already been decided to fire.
/// </para>
/// </remarks>
internal static class EffectOpResolver
{
    /// <summary>Resolves one `18` §2 op.</summary>
    /// <param name="effect">The effect, already gated and triggered.</param>
    /// <param name="context">The firing context and the seams.</param>
    /// <exception cref="EffectContextException">
    /// The effect is malformed for its op, a value mode has no basis, a `18` §5 token's subject is
    /// absent, or a seam is unwired. The layer's one failure type — see
    /// <see cref="EffectContextException"/>.
    /// </exception>
    internal static EffectOpOutcome Resolve(EffectDefinition effect, EffectOpContext context)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(context);

        return effect.Op switch
        {
            // ── §2.1 stat (6) · applied by 18 §8's aggregation, never by a firing effect.
            EffectOp.STAT_ADD_FLAT or
            EffectOp.STAT_ADD_PCT or
            EffectOp.STAT_MULT or
            EffectOp.STAT_SET or
            EffectOp.STAT_CONVERT or
            EffectOp.STAT_CAP_OVERRIDE => Aggregated(effect),

            // ── §2.2 damage and healing (7) · routed by 05 §4.2.
            EffectOp.DAMAGE => Damaged(effect, DamageAndHealingOps.Damage(effect, context)),
            EffectOp.DAMAGE_TRUE => Resolved(effect, DamageAndHealingOps.DamageTrue(effect, context)),
            EffectOp.DAMAGE_MAXHP_PCT => Resolved(effect, DamageAndHealingOps.DamageMaxHpPct(effect, context)),
            EffectOp.HEAL => Resolved(effect, DamageAndHealingOps.Heal(effect, context)),
            EffectOp.HEAL_LEECH => Resolved(effect, DamageAndHealingOps.HealLeech(effect, context)),
            EffectOp.SHIELD => Resolved(effect, DamageAndHealingOps.Shield(effect, context)),
            EffectOp.REFLECT => Resolved(effect, DamageAndHealingOps.Reflect(effect, context)),

            // ── §2.3 status (6).
            EffectOp.APPLY_STATUS => Resolved(effect, StatusOps.Apply(effect, context)),
            EffectOp.REMOVE_STATUS => Resolved(effect, StatusOps.Remove(effect, context)),
            EffectOp.EXTEND_STATUS => Resolved(effect, StatusOps.Extend(effect, context)),
            EffectOp.IMMUNE_STATUS => Resolved(effect, StatusOps.GrantImmunity(effect, context)),
            EffectOp.STATUS_POWER_PCT => Resolved(effect, StatusOps.ScaleOutgoingPower(effect, context)),
            EffectOp.STATUS_DURATION_PCT => Resolved(effect, StatusOps.ScaleIncomingDuration(effect, context)),

            // ── §2.4 combat-flow (12).
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

            // ── §2.5 run and board (13) · queued, never resolved.
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

    private static EffectOpOutcome Resolved(EffectDefinition effect, double amount) =>
        new(effect.Op, amount, OpDisposition.RESOLVED);

    private static EffectOpOutcome Damaged(EffectDefinition effect, DamageAndHealingOps.DamageTotals totals) =>
        new(effect.Op, totals.HpLost, OpDisposition.RESOLVED, totals.Basis);

    private static EffectOpOutcome Aggregated(EffectDefinition effect) =>
        new(effect.Op, 0.0, OpDisposition.AGGREGATED);
}
