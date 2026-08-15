using System.Globalization;
using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// Eleven of the twelve combat-flow ops. The twelfth, <c>STAT_COPY</c>, is <see cref="StatCopyOp"/> —
/// the one op whose <c>target</c> does not name who it writes to.
/// </summary>
/// <remarks>
/// These ops write actor state rather than HP: charge counters, cooldowns, targeting weight, an
/// armed death save. None has a number that means anything to the damage pipeline, which is why they
/// route through <see cref="ICombatFlowSink"/> and not <see cref="IAttackPipeline"/>.
/// </remarks>
internal static class CombatFlowOps
{
    /// <summary><c>EXTRA_ATTACK</c>: perform an additional attack immediately, <c>value</c> times.</summary>
    /// <remarks>The count is <c>value</c>, not <c>charges</c> — <c>PK_FLURRY</c> already authors it that way.</remarks>
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

    /// <summary><c>ATTACK_MULT_NEXT</c>: multiply the damage of the next <c>charges</c> attacks by <c>value</c>.</summary>
    /// <remarks>Holder-scoped. The effect id travels because charges are consumed in ascending effect-id order.</remarks>
    internal static double AttackMultiplierCharges(EffectDefinition effect, EffectOpContext context)
    {
        var multiplier = OpRounding.Round(
            context.Seams.Values.ScaledValue(effect), effect.Id, "attack multiplier");

        context.Seams.Flow.GrantAttackMultiplierCharges(
            OpTargets.Holder(context), multiplier, RequireCharges(effect), effect.Id);

        return multiplier;
    }

    /// <summary><c>FORCE_CRIT_NEXT</c>: the next <c>charges</c> attacks always crit.</summary>
    /// <remarks>
    /// Carries no value — crit size is the actor's own CDMG. An effect that authors one is refused
    /// rather than silently ignored, since a stray value would be a misreading nobody caught.
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

    /// <summary><c>REDUCE_COOLDOWN</c>: reduce pet/boss ability cooldowns.</summary>
    /// <remarks>
    /// The value is a fraction of the cooldown, not seconds — the only authored user ("−3% per rank")
    /// is a percentage, and reading it as seconds would barely dent a 12s cooldown.
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

    /// <summary><c>SURVIVE_LETHAL</c>: arm a save at the HP the effect names.</summary>
    /// <remarks>
    /// <c>valueMode</c> lets this be authored either as a flat HP value or a fraction of Max HP.
    /// Holder-scoped, and not an <c>ON_REVIVE</c> — the actor never died, so the seam has a separate
    /// member for it.
    /// </remarks>
    internal static double SurviveLethal(EffectDefinition effect, EffectOpContext context)
    {
        var holder = OpTargets.Holder(context);
        var hp = SurvivalHp(effect, context, OpValueRules.SurviveLethal);

        context.Seams.Flow.ArmSurviveLethal(holder, hp, effect.Id);

        return hp;
    }

    /// <summary><c>REVIVE</c>: return from 0 HP at a fraction of Max HP. Fires <c>ON_REVIVE</c>.</summary>
    internal static double Revive(EffectDefinition effect, EffectOpContext context)
    {
        var holder = OpTargets.Holder(context);
        var hp = SurvivalHp(effect, context, OpValueRules.Revive);

        context.Seams.Flow.ArmRevive(holder, hp, effect.Id);

        return hp;
    }

    /// <summary><c>SUMMON</c>: <c>value</c> of <c>archetype</c>, at most <c>maxAlive</c>.</summary>
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
    /// <c>RANDOM_OUTCOME</c>: one draw over the <c>outcomes</c> weight table, naming the winning
    /// effect id. Returns the 1-based index of the row that won.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Exists for mutually-exclusive weighted outcomes (e.g. one visible d6 result): three
    /// independently chance-gated effects would be three separate draws where any combination could
    /// fire, spending three RNG draws where a single pick spends one — desynchronising every later
    /// draw of the battle between client and server.
    /// </para>
    /// <para>
    /// Validation runs before the draw: a rejected call must not consume a draw index, or a malformed
    /// table would shift every later draw of the battle.
    /// </para>
    /// </remarks>
    /// <param name="effect">The authored roll, carrying the <c>outcomes</c> table.</param>
    /// <param name="context">The evaluation state and the seams the winner is named across.</param>
    /// <returns>The 1-based index of the row that won.</returns>
    /// <exception cref="EffectContextException">
    /// The table is malformed, or the context carries no draw stream.
    /// </exception>
    internal static double RandomOutcome(EffectDefinition effect, EffectOpContext context)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(context);

        var problems = EffectOpValidation.Problems(effect);
        if (problems.Count > 0)
        {
            throw new EffectContextException(
                effect.Id,
                string.Join("; ", problems),
                "`18` §10.1 E6's outcomes table IS the op, so a malformed one has nothing to roll. " +
                "It is refused BEFORE the draw because DeterministicRng.Position is the persisted " +
                "state of the stream — a rejected call that spent an index would shift every later " +
                "draw of the battle between the client and the server.");
        }

        var rng = context.Evaluation.Rng ?? throw new EffectContextException(
            nameof(EffectOp.RANDOM_OUTCOME),
            "the context carries no draw stream",
            "`14` §8.1: a combat draw is new DeterministicRng(battleSeed, RngStreams.Combat), and " +
            "the battleSeed is handed in by the simulator. Answering with the first row instead " +
            "would be a stable, reproducible, wrong 'random' — `18` §5's RANDOM_ENEMY is refused " +
            "for the same reason.");

        // Validation above has already refused a null, short, duplicated or unweighted table, so
        // every row below is one 14 §8.0 can walk.
        var outcomes = effect.Outcomes!;
        var table = new (string Item, double Weight)[outcomes.Count];

        for (var row = 0; row < outcomes.Count; row++)
        {
            table[row] = (outcomes[row].EffectId, outcomes[row].Weight);
        }

        var chosen = rng.WeightedPick(table);

        context.Seams.Flow.RandomOutcome(OpTargets.Holder(context), chosen, effect.Id);

        for (var row = 0; row < outcomes.Count; row++)
        {
            if (string.Equals(outcomes[row].EffectId, chosen, StringComparison.Ordinal))
            {
                // 1-based, so the amount is the face shown rather than an array offset nobody authored.
                return row + 1;
            }
        }

        throw new EffectContextException(
            effect.Id,
            $"its weighted walk answered '{chosen}', which is not a row of its own table",
            "14 §8.0's WeightedPick returns an item OF the table it was handed, so this is " +
            "unreachable — and it is stated rather than assumed because the alternative is returning " +
            "an index nobody computed (steering S6).");
    }

    /// <summary><c>CLEAR_SUMMONS</c>: despawn all living summons owned by the target (default SELF).</summary>
    /// <remarks>
    /// Despawned, not killed — no <c>ON_DEATH</c>/<c>ON_KILL</c>/on-death explosions/rewards, so a
    /// summoner clearing its own adds can't detonate them.
    /// </remarks>
    internal static double ClearSummons(EffectDefinition effect, EffectOpContext context)
    {
        foreach (var owner in OpTargets.OrSelf(effect, context))
        {
            context.Seams.Flow.ClearSummons(owner, effect.Id);
        }

        return 0.0;
    }

    /// <summary><c>SET_TARGET_PRIORITY</c>: default 0, -1 deprioritises, +1 forces focus.</summary>
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

    /// <summary><c>DAMAGE_TAKEN_MULT</c>: multiply incoming damage.</summary>
    /// <remarks>
    /// Accumulates as the product of every active one in ascending effect-id order, rather than
    /// replacing. A non-positive multiplier is refused — zero would be permanent invulnerability and
    /// a negative one would heal the defender through the damage pipeline.
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

    /// <summary>The charges — the N of "the next N attacks".</summary>
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
