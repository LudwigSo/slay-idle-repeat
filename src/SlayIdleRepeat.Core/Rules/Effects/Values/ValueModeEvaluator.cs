using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Values;

/// <summary>
/// The eight <c>valueMode</c>s: what an effect's <c>value</c> is a multiple of.
/// </summary>
/// <remarks>
/// Answers one question — what amount does this value denote, given these subjects — and knows
/// nothing about damage pipelines, ward pools or heal ceilings. Nothing here rounds: a value-mode
/// resolution is an input to the damage/heal/stat-aggregation steps that round, not one of those
/// steps itself, so rounding here would be an earlier, incorrect accumulation point. A mode whose
/// subject is absent throws (a token whose subject is present but empty resolves to an empty set
/// instead) — the headline case is <c>HEAL_AMOUNT</c>/<c>OVERHEAL_AMOUNT</c>, which only exist
/// inside <c>ON_HEAL</c> contexts, where a silent zero would be indistinguishable from a legitimate
/// zero heal. Subjects are plain numbers and an actor view, never a stat block, since this namespace
/// sits below <c>Rules.Stats</c> in the layering and can't reference it.
/// </remarks>
internal static class ValueModeEvaluator
{
    /// <summary>The default <c>valueMode</c> for damage-and-healing ops: <c>ATK_MULT</c>.</summary>
    /// <remarks>
    /// The default is not applied by this type — <see cref="EffectDefinition.ValueMode"/> is left
    /// <c>null</c> rather than coerced, and each damage/heal op applies
    /// <c>effect.ValueMode ?? DamageAndHealingDefault</c> itself.
    /// </remarks>
    internal const ValueMode DamageAndHealingDefault = ValueMode.ATK_MULT;

    /// <summary>The amount <paramref name="value"/> denotes under <paramref name="mode"/>.</summary>
    /// <param name="mode">One of the eight value modes.</param>
    /// <param name="value">The effect's value, already scaled if it carries a scale.</param>
    /// <param name="subjects">The subjects the mode reads.</param>
    /// <param name="effectId">The effect's id, named in any failure.</param>
    /// <exception cref="EffectContextException">
    /// The bundle does not carry the mode's subject, or the mode is not one of the eight.
    /// </exception>
    internal static double Resolve(
        ValueMode mode, double value, ValueModeSubjects subjects, string effectId) =>
        mode switch
        {
            ValueMode.ATK_MULT => value * Amount(
                subjects.SourceAttack, mode, effectId,
                "the effect's source has no resolved ATK",
                "18 §2.2: 'value is a multiple of the source's ATK unless valueMode says otherwise'. " +
                "05 §4.2 adds that a pet ability uses the HERO's ATK, so the number is the caller's " +
                "to resolve and there is no second reading of it here."),

            // The one mode with no subject at all — resolved at stat aggregation time, where there
            // is no target, attacker or heal in context.
            ValueMode.FLAT => value,

            ValueMode.SELF_MAXHP_PCT => value * Actor(
                subjects.Source, mode, effectId, "source",
                "18 §7.10's Ossify wards 20% of SELF_MAXHP_PCT.").MaxHp,

            ValueMode.TARGET_MAXHP_PCT => value * Actor(
                subjects.Target, mode, effectId, "target",
                "18 §7.10's Volatile elite explodes for 15% of TARGET_MAXHP_PCT.").MaxHp,

            ValueMode.TARGET_MISSING_HP_PCT => value * MissingHp(
                Actor(
                    subjects.Target, mode, effectId, "target",
                    "18 §2.2 types it as a fraction of the target's missing HP.")),

            ValueMode.DAMAGE_DEALT_PCT => value * Amount(
                subjects.DamageDealt, mode, effectId,
                "no damage has just been dealt in this context",
                "18 §2.2 types it as a fraction of the damage just dealt — HEAL_LEECH's basis. " +
                "A zero would be a lifesteal proc off an attack that never landed."),

            ValueMode.HEAL_AMOUNT => value * Amount(
                subjects.HealAmount, mode, effectId,
                "the context is not an ON_HEAL context",
                HealOnly),

            ValueMode.OVERHEAL_AMOUNT => value * Amount(
                subjects.OverhealAmount, mode, effectId,
                "the context is not an ON_HEAL context",
                HealOnly),

            // Not an amount at all: SURVIVE_LETHAL consumes NEGATE before value resolution, so a
            // NEGATE that reaches this evaluator is a caller about to spend a number that does not
            // exist — refused by name rather than answered.
            ValueMode.NEGATE => throw new EffectContextException(
                nameof(ValueMode.NEGATE),
                $"'{effectId}' asks for its amount, and NEGATE is not an amount mode",
                "16 D49: NEGATE voids the lethal hit and HP is unchanged — there is no HP number. " +
                "SURVIVE_LETHAL arms the save without resolving a value, so nothing may reach this " +
                "evaluator with it."),

            _ => throw new EffectContextException(
                mode.ToString(),
                $"'{effectId}' names a value mode that is not one of 18 §2.2's eight",
                "18 §2.2: ATK_MULT · FLAT · SELF_MAXHP_PCT · TARGET_MAXHP_PCT · " +
                "TARGET_MISSING_HP_PCT · DAMAGE_DEALT_PCT · HEAL_AMOUNT · OVERHEAL_AMOUNT."),
        };

    /// <summary>
    /// The reference message both heal modes carry, stated once so the two can't drift apart.
    /// </summary>
    /// <remarks>
    /// Can't be a schema rule: the <c>ON_HEAL</c> context can also be supplied by the wrapper an
    /// effect list sits in, since some blocks carry no trigger of their own — so it's enforced here,
    /// at evaluation.
    /// </remarks>
    private const string HealOnly =
        "18 §2.2: HEAL_AMOUNT and OVERHEAL_AMOUNT 'exist only inside ON_HEAL contexts (05 §4.3)'. " +
        "A zero here would be indistinguishable from a legitimate zero heal — 05 §4.3's Heal() on a " +
        "full-HP target heals 0 and overheals the whole amount — so the quiet answer is the one that " +
        "could never go red.";

    private static double Amount(
        double? reading, ValueMode mode, string effectId, string missing, string reference) =>
        reading ?? throw new EffectContextException(
            mode.ToString(), $"'{effectId}' reads it and {missing}", reference);

    private static IEffectActorView Actor(
        IEffectActorView? actor, ValueMode mode, string effectId, string role, string reference) =>
        actor ?? throw new EffectContextException(
            mode.ToString(), $"'{effectId}' reads the {role}'s Max HP and the context carries no {role}",
            reference);

    /// <summary>A fraction of the target's missing HP.</summary>
    /// <remarks>
    /// Floored at zero, mirroring the same clamp <c>ConditionEvaluator.HpFraction</c> applies to
    /// <c>SELF_MISSING_HP_PCT</c> — the DSL states "missing HP" twice (once as a condition function,
    /// once as a value mode) and the two must agree about the same actor. The case that reaches it: a
    /// Max HP decrease (e.g. a buff expiring) can leave <c>CurrentHp &gt; MaxHp</c>, where unclamped
    /// "missing HP" would go negative and an execute effect would heal its target instead.
    /// </remarks>
    private static double MissingHp(IEffectActorView target) =>
        Math.Max(0.0, target.MaxHp - target.CurrentHp);
}

/// <summary>
/// The subjects the eight value modes read — assembled by the caller, never gathered here.
/// </summary>
/// <remarks>
/// Every member is nullable and absent by default, because which subjects exist is what
/// distinguishes an <c>ON_HEAL</c> cascade from a stat aggregation. Defaulting the numbers to zero
/// instead would make the <c>ON_HEAL</c>-only restriction unenforceable.
/// </remarks>
internal readonly record struct ValueModeSubjects
{
    /// <summary>The effect's source actor — <c>SELF_MAXHP_PCT</c>'s subject.</summary>
    internal IEffectActorView? Source { get; init; }

    /// <summary>
    /// The actor the effect is landing on — <c>TARGET_MAXHP_PCT</c> and
    /// <c>TARGET_MISSING_HP_PCT</c>'s subject.
    /// </summary>
    internal IEffectActorView? Target { get; init; }

    /// <summary>The source's final resolved ATK — <c>ATK_MULT</c>'s subject.</summary>
    /// <remarks>
    /// A number rather than a reading off <see cref="Source"/>: a pet ability uses the hero's ATK
    /// with the pet's own CRIT, so the source actor isn't always the actor whose ATK applies.
    /// <see cref="IEffectActorView"/> carries no ATK, same as it carries no cooldown — it's a
    /// targeting/condition view, not the stat block.
    /// </remarks>
    internal double? SourceAttack { get; init; }

    /// <summary>The damage just dealt — <c>DAMAGE_DEALT_PCT</c>'s subject.</summary>
    internal double? DamageDealt { get; init; }

    /// <summary>The amount healed — <c>HEAL_AMOUNT</c>'s subject, in an <c>ON_HEAL</c> context.</summary>
    internal double? HealAmount { get; init; }

    /// <summary>The overheal — <c>OVERHEAL_AMOUNT</c>'s subject, likewise.</summary>
    internal double? OverhealAmount { get; init; }
}
