using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Values;

/// <summary>
/// 🔒 `18` §2.2 — the eight <c>valueMode</c>s: what an effect's <c>value</c> is a multiple of.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>M2-06 owns the evaluator; M2-03 owns each op's use of the result.</b> This type answers one
/// question — <em>"what amount does this value denote, given these subjects"</em> — and knows nothing
/// about damage pipelines, ward pools or heal ceilings. That split is why the signature takes a
/// <see cref="ValueMode"/> and a bundle of subjects rather than an op and a battle.
/// </para>
/// <para>
/// 🔒 <b>Nothing here rounds.</b> `05` §1.1's accumulation points are <em>"after each damage
/// calculation, each heal, and each stat aggregation step"</em>, and a value-mode resolution is none
/// of the three: it is an <em>input</em> to the first two, which round at their own step. Rounding
/// here would be a second, earlier accumulation point the document does not authorise — the same
/// mistake, in the same layer, that <c>EffectStackSet</c> records for the multiplicative product,
/// where it is worth 0.0012 ATK a second on every boss.
/// </para>
/// <para>
/// 🔒 <b>A mode whose SUBJECT is absent throws.</b> That is the uniform rule of this layer, stated
/// once by <see cref="EffectContextException"/>: <em>a token whose subject is absent throws; a token
/// whose subject is present but whose set is empty resolves to the empty set</em>. The headline case
/// is `18` §2.2's own restriction — <c>HEAL_AMOUNT</c> and <c>OVERHEAL_AMOUNT</c> <em>"exist only
/// inside <c>ON_HEAL</c> contexts (`05` §4.3)"</em> — where a silent zero would be byte-identical to
/// a legitimate zero heal, because <c>Heal()</c> on a full-HP target heals exactly 0 and overheals
/// the lot. Every message names the effect, so a failure says which rule fired (steering S2).
/// </para>
/// <para>
/// ⚠️ <b>The subjects are plain numbers and M2-05's actor view, never a stat block.</b> R17 fixes the
/// intra-<c>Rules</c> layering as <c>Rules.Combat → Rules.Stats → Rules.Effects</c> with this
/// namespace at the bottom, so <c>ActorStats</c> is not nameable from here — and
/// <see cref="IEffectActorView"/> deliberately carries no ATK. Hence
/// <see cref="ValueModeSubjects.SourceAttack"/> as a <see cref="double"/>: the caller has already
/// resolved it through `18` §8.
/// </para>
/// </remarks>
internal static class ValueModeEvaluator
{
    /// <summary>
    /// `18` §2.2: <em>"<c>valueMode</c>: <c>ATK_MULT</c> (default)"</em> — stated once, here.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>The default is not applied by this type.</b> §2.2 states it inside the damage-and-healing
    /// section, and `18` says nothing about what an <em>absent</em> <c>valueMode</c> means for an op
    /// outside it — which is why <see cref="EffectDefinition.ValueMode"/> is left <c>null</c> rather
    /// than coerced. So the constant is offered and the op applies it:
    /// <c>effect.ValueMode ?? ValueModeEvaluator.DamageAndHealingDefault</c>, at the §2.2 ops and
    /// nowhere else.
    /// </remarks>
    internal const ValueMode DamageAndHealingDefault = ValueMode.ATK_MULT;

    /// <summary>The amount <paramref name="value"/> denotes under <paramref name="mode"/>.</summary>
    /// <param name="mode">One of `18` §2.2's eight modes.</param>
    /// <param name="value">The effect's value, already scaled by `18` §1.1 if it carries a scale.</param>
    /// <param name="subjects">The subjects the mode reads.</param>
    /// <param name="effectId">The effect's `18` §8 id, named in any failure (steering S2).</param>
    /// <exception cref="EffectContextException">
    /// The bundle does not carry the mode's subject, or the mode is outside `18` §2.2's eight.
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

            // 🔒 The one mode with no subject at all. `18` §9.1's CP_GLASS_HEART is resolved at stat
            // aggregation time, where there is no target, no attacker and no heal.
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

            _ => throw new EffectContextException(
                mode.ToString(),
                $"'{effectId}' names a value mode that is not one of 18 §2.2's eight",
                "18 §2.2: ATK_MULT · FLAT · SELF_MAXHP_PCT · TARGET_MAXHP_PCT · " +
                "TARGET_MISSING_HP_PCT · DAMAGE_DEALT_PCT · HEAL_AMOUNT · OVERHEAL_AMOUNT."),
        };

    /// <summary>
    /// 🔒 The reference both heal modes carry. `18` §2.2 and `05` §4.3 restrict them together, and
    /// stating the restriction once means the two cannot drift apart.
    /// </summary>
    /// <remarks>
    /// It cannot be a schema rule — <see cref="ValueMode"/>'s own remarks record why: the
    /// <c>ON_HEAL</c> context can also be supplied by the wrapper an effect list sits in, since a pet
    /// <c>active</c> block carries no trigger of its own (`18` §7.7). So it is enforced here, at
    /// evaluation.
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

    /// <summary>
    /// `18` §2.2's <em>"a fraction of the target's missing HP"</em>.
    /// </summary>
    /// <remarks>
    /// 🔒 Floored at zero, for the reason <c>ConditionEvaluator.HpFraction</c> gives for clamping:
    /// a Max HP <em>decrease</em> (a buff expiring, `18` §9.1's <c>CP_GLASS_HEART</c> re-base) leaves
    /// <c>CurrentHp &gt; MaxHp</c>, at which point "missing HP" is negative and an execute effect
    /// would <em>heal</em> its target. The document types the quantity as HP the target has lost, and
    /// a target that has lost none has lost zero.
    /// </remarks>
    private static double MissingHp(IEffectActorView target) =>
        Math.Max(0.0, target.MaxHp - target.CurrentHp);
}

/// <summary>
/// The subjects `18` §2.2's eight modes read — assembled by the caller, never gathered here.
/// </summary>
/// <remarks>
/// Every member is nullable and every one is absent by default, because <em>which</em> subjects exist
/// is what distinguishes an <c>ON_HEAL</c> cascade from a stat aggregation. A bundle that defaulted
/// its numbers to zero would make `18` §2.2's <c>ON_HEAL</c>-only restriction unenforceable.
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

    /// <summary>
    /// The source's final resolved ATK (`18` §8) — <c>ATK_MULT</c>'s subject.
    /// </summary>
    /// <remarks>
    /// A number rather than a reading off <see cref="Source"/>: `05` §4.2 rules that a pet ability
    /// uses the <b>hero's</b> ATK with the pet's own CRIT, so the source actor is not always the
    /// actor whose ATK applies. <see cref="IEffectActorView"/> carries no ATK for the same reason it
    /// carries no cooldown — it is `18` §4/§5's view, not the stat block.
    /// </remarks>
    internal double? SourceAttack { get; init; }

    /// <summary>The damage just dealt — <c>DAMAGE_DEALT_PCT</c>'s subject.</summary>
    internal double? DamageDealt { get; init; }

    /// <summary>`05` §4.3's <c>healed</c> — <c>HEAL_AMOUNT</c>'s subject, in an <c>ON_HEAL</c> context.</summary>
    internal double? HealAmount { get; init; }

    /// <summary>`05` §4.3's <c>overheal</c> — <c>OVERHEAL_AMOUNT</c>'s subject, likewise.</summary>
    internal double? OverhealAmount { get; init; }
}
