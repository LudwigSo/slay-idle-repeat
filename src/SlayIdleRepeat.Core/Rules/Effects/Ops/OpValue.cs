using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary>
/// 🔒 `18` §2.2's <c>valueMode</c> — <em>"what <c>value</c> is a multiple of"</em> — applied to one
/// op's scaled value.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Each op declares which modes it admits and what its own default is.</b> `18` §2.2 states
/// one global default (<c>ATK_MULT</c>) and then contradicts it in three of its own rows:
/// <c>DAMAGE_MAXHP_PCT</c> is <em>"damage as a percentage of the target's Max HP"</em>,
/// <c>HEAL_LEECH</c> is <em>"heal a % of damage just dealt"</em>, and `05` §1 types <c>THORN</c> —
/// what <c>REFLECT</c> writes — as a fraction, not an ATK multiple. Applying the global default
/// uniformly would make a 25% thorns effect scale with attack power and the Volatile elite's
/// explosion deal 15% of <em>its own ATK</em> instead of 15% of the hero's Max HP, which `18` §7.10
/// captions in words. So the op's own row wins where it states a unit, and where neither states one
/// the op refuses (steering S6). The whole table is in <see cref="OpValueRules"/>.
/// </para>
/// <para>
/// 🔒 <b>The scaling has already happened.</b> The input is
/// <see cref="IScaledValueReader.ScaledValue"/> — `18` §1.1's <c>value × steps</c>, M2-06's half.
/// This is the second multiplication only.
/// </para>
/// </remarks>
internal static class OpValue
{
    /// <summary>
    /// The number an op applies: its scaled value, multiplied by whatever `18` §2.2's mode makes it
    /// a multiple of, rounded to 4 dp (`05` §1.1).
    /// </summary>
    /// <param name="effect">The authored effect.</param>
    /// <param name="context">The firing context — the holder, the seams and the event's readings.</param>
    /// <param name="target">
    /// The actor the op is resolving against, for the two target-relative modes. <c>null</c> where
    /// the op has no target (a self-scoped charge grant), which makes those two modes throw.
    /// </param>
    /// <param name="rules">Which modes this op admits, and its default.</param>
    /// <exception cref="EffectContextException">
    /// The mode is not one the op admits, or its basis is absent from the context.
    /// </exception>
    internal static double Amount(
        EffectDefinition effect, EffectOpContext context, IEffectActorView? target, OpValueRules rules)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(rules);

        var mode = ModeOf(effect, rules);
        var scaled = context.Seams.Values.ScaledValue(effect);

        return OpRounding.Round(scaled * Basis(effect, context, target, mode), effect.Id, $"{mode} value");
    }

    /// <summary>
    /// The `18` §2.2 mode this effect resolves under — the authored one, or the op's own default.
    /// </summary>
    /// <exception cref="EffectContextException">The op does not admit the authored mode.</exception>
    internal static ValueMode ModeOf(EffectDefinition effect, OpValueRules rules)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(rules);

        var mode = effect.ValueMode ?? rules.Default;

        return rules.Admits.Contains(mode)
            ? mode
            : throw new EffectContextException(
                effect.Id,
                $"{effect.Op} does not admit valueMode {mode}",
                $"18 §2.2 gives {effect.Op} the modes [{string.Join(", ", rules.Admits)}] — " +
                $"{rules.Reason} Its default is {rules.Default}. A mode outside that set has no " +
                "stated meaning for this op, and picking one would be a rule nobody authored " +
                "(steering S6).");
    }

    /// <summary>What the mode makes <c>value</c> a multiple of.</summary>
    private static double Basis(
        EffectDefinition effect, EffectOpContext context, IEffectActorView? target, ValueMode mode) =>
        mode switch
        {
            // 🔒 The source's ATK, read through the snapshot seam — R17 keeps Rules.Stats out of
            // reach, and 18 §2.2's "the source's ATK" is the holder's, never the target's.
            ValueMode.ATK_MULT => context.Seams.Stats.FinalStat(context.Holder, StatId.ATK),

            ValueMode.FLAT => 1.0,

            ValueMode.SELF_MAXHP_PCT => context.Holder.MaxHp,

            ValueMode.TARGET_MAXHP_PCT => Targeted(effect, target, mode).MaxHp,

            // 🔒 Missing HP, floored at zero. A target healed above its own Max HP by a mid-tick
            // grant would otherwise give a NEGATIVE missing fraction, and a "damage" op would heal.
            ValueMode.TARGET_MISSING_HP_PCT => MissingHp(Targeted(effect, target, mode)),

            ValueMode.DAMAGE_DEALT_PCT => context.DamageDealt ?? throw Missing(
                effect, mode, "the firing event dealt no damage",
                "05 §4 step 8's on-damage basis exists only inside a resolved hit. M2-04's on-hit " +
                "family supplies it; outside one there is no damage to take a percentage of, and 0 " +
                "would spell 'the hit was fully absorbed' — which 05 §4.1 says a leech still heals off."),

            ValueMode.HEAL_AMOUNT => context.HealAmount ?? throw Missing(
                effect, mode, "the context is not an ON_HEAL context",
                "18 §2.2: HEAL_AMOUNT and OVERHEAL_AMOUNT 'exist only inside ON_HEAL contexts (05 §4.3)'."),

            ValueMode.OVERHEAL_AMOUNT => context.OverhealAmount ?? throw Missing(
                effect, mode, "the context is not an ON_HEAL context",
                "18 §2.2: HEAL_AMOUNT and OVERHEAL_AMOUNT 'exist only inside ON_HEAL contexts (05 §4.3)'. " +
                "0 would silently delete PK_TRANSFUSION's shield rather than reporting that the " +
                "trigger did not carry the reading."),

            _ => throw new EffectContextException(
                effect.Id,
                $"valueMode {mode} is not one of 18 §2.2's eight",
                "18 §2.2: ATK_MULT · FLAT · SELF_MAXHP_PCT · TARGET_MAXHP_PCT · " +
                "TARGET_MISSING_HP_PCT · DAMAGE_DEALT_PCT · HEAL_AMOUNT · OVERHEAL_AMOUNT."),
        };

    private static double MissingHp(IEffectActorView actor) => Math.Max(0.0, actor.MaxHp - actor.CurrentHp);

    private static IEffectActorView Targeted(
        EffectDefinition effect, IEffectActorView? target, ValueMode mode) =>
        target ?? throw Missing(
            effect, mode, "the op resolved against no actor",
            "18 §2.2's two target-relative modes read the actor the op is applying to; an op with no " +
            "target has nothing to read. 05 §3.1 keeps a dead actor out of every set token (18 §5), " +
            "so an empty target set is a legitimate outcome — but it never reaches a value.");

    private static EffectContextException Missing(
        EffectDefinition effect, ValueMode mode, string missing, string reference) =>
        new(effect.Id, $"valueMode {mode} has no basis — {missing}", reference);
}

/// <summary>
/// Which `18` §2.2 value modes one op admits, and which it falls back to. One instance per op, all
/// of them in <see cref="OpValueRules"/>'s static members with the clause each is read from.
/// </summary>
/// <param name="Default">The mode used when the effect authors none.</param>
/// <param name="Admits">Every mode the op admits, including <paramref name="Default"/>.</param>
/// <param name="Reason">The document clause the set and the default are read from.</param>
internal sealed record OpValueRules(
    ValueMode Default, IReadOnlySet<ValueMode> Admits, string Reason)
{
    /// <summary>Every mode that produces an HP-shaped amount from a source, a target or an event.</summary>
    private static readonly IReadOnlySet<ValueMode> AmountModes = new HashSet<ValueMode>
    {
        ValueMode.ATK_MULT, ValueMode.FLAT, ValueMode.SELF_MAXHP_PCT, ValueMode.TARGET_MAXHP_PCT,
        ValueMode.TARGET_MISSING_HP_PCT, ValueMode.DAMAGE_DEALT_PCT,
    };

    /// <summary>The same, plus the two `05` §4.3 readings that exist only inside <c>ON_HEAL</c>.</summary>
    private static readonly IReadOnlySet<ValueMode> AmountOrHealReadingModes =
        new HashSet<ValueMode>(AmountModes)
        {
            ValueMode.HEAL_AMOUNT, ValueMode.OVERHEAL_AMOUNT,
        };

    /// <summary>
    /// 🔒 <c>DAMAGE</c> — `05` §4.2 spends the value on the <c>AttackMultiplier</c> itself:
    /// <em>"the op's <c>value</c> <b>is</b> the AttackMultiplier for that resolved attack
    /// (<c>ATK_MULT</c> mode)"</em>. So the op multiplies by nothing here; <c>ResolveAttack</c>
    /// applies <c>attacker.ATK</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>Validation only — <see cref="OpValue.Amount"/> is never called with this.</b>
    /// <c>DamageOp</c> checks the mode through <see cref="OpValue.ModeOf"/> and then passes the
    /// <em>scaled value itself</em> to <see cref="IAttackPipeline.ResolveAttack"/>, because that is
    /// what `05` §4 step 2 multiplies by <c>attacker.ATK</c>. Running it through
    /// <see cref="OpValue.Amount"/> would apply ATK twice.
    /// </para>
    /// <para>
    /// ⚠️ Every other mode is <b>refused</b>, and that is a ruling. A <c>FLAT</c> <c>DAMAGE</c>
    /// would have to be turned into a multiplier by dividing by the attacker's ATK, which `05` §4.2
    /// does not authorise and which would make the same effect hit differently on two builds for
    /// reasons no document states. No authored effect in `18` uses one.
    /// </para>
    /// </remarks>
    internal static OpValueRules Damage { get; } = new(
        ValueMode.ATK_MULT,
        new HashSet<ValueMode> { ValueMode.ATK_MULT },
        "05 §4.2 makes DAMAGE's value the AttackMultiplier itself — ResolveAttack applies " +
        "attacker.ATK, so the op must not.");

    /// <summary>
    /// <c>DAMAGE_TRUE</c> — an HP amount, on `18` §2.2's stated global default of
    /// <c>ATK_MULT</c>. `05` §4.2: <em>"HP is reduced directly"</em>.
    /// </summary>
    internal static OpValueRules DamageTrue { get; } = new(
        ValueMode.ATK_MULT, AmountModes,
        "18 §2.2: 'value is a multiple of the source's ATK unless valueMode says otherwise', and " +
        "05 §4.2 reduces HP by it directly.");

    /// <summary>
    /// 🔒 <c>DAMAGE_MAXHP_PCT</c> — <c>TARGET_MAXHP_PCT</c>, from the op's <b>own</b> §2.2 row:
    /// <em>"damage as a percentage of the <b>target's</b> Max HP"</em>.
    /// </summary>
    /// <remarks>
    /// Both authored users agree and neither would work under the global <c>ATK_MULT</c> default:
    /// `18` §7.10's Volatile elite is captioned <em>"explodes on death for 15% of hero Max HP"</em>
    /// and writes <c>TARGET_MAXHP_PCT</c> explicitly, and §7.5's <c>CP_BLOOD_PRICE</c> is
    /// <em>"lose 3% Max HP after every battle"</em> on <c>target: SELF</c> with no mode at all —
    /// which is the same rule, since the target is the holder. Recorded as errata against §2.2's
    /// blanket default.
    /// </remarks>
    internal static OpValueRules DamageMaxHpPct { get; } = new(
        ValueMode.TARGET_MAXHP_PCT,
        new HashSet<ValueMode> { ValueMode.TARGET_MAXHP_PCT, ValueMode.SELF_MAXHP_PCT },
        "18 §2.2's own row for this op names the unit — a percentage of the target's Max HP — and " +
        "both authored users (18 §7.10's Volatile, §7.5's CP_BLOOD_PRICE) read that way.");

    /// <summary><c>HEAL</c> — an HP amount; §2.2's stated default applies, and `05` §4.3 scales by <c>HEAL%</c>.</summary>
    internal static OpValueRules Heal { get; } = new(
        ValueMode.ATK_MULT, AmountOrHealReadingModes,
        "18 §2.2: 'heal a flat amount or a % of Max HP', with the section's stated ATK_MULT default " +
        "where the author writes neither.");

    /// <summary>
    /// 🔒 <c>HEAL_LEECH</c> — <c>DAMAGE_DEALT_PCT</c>, and nothing else. Its §2.2 row <em>is</em> the
    /// mode: <em>"heal a % of damage just dealt"</em>.
    /// </summary>
    internal static OpValueRules HealLeech { get; } = new(
        ValueMode.DAMAGE_DEALT_PCT,
        new HashSet<ValueMode> { ValueMode.DAMAGE_DEALT_PCT },
        "18 §2.2's row for this op states the basis outright — 'a % of damage just dealt' — so no " +
        "other mode has a meaning here.");

    /// <summary><c>SHIELD</c> — a ward amount. Every authored user writes its mode explicitly.</summary>
    internal static OpValueRules Shield { get; } = new(
        ValueMode.ATK_MULT, AmountOrHealReadingModes,
        "18 §2.2's SHIELD grants 'a WARD absorb of a given size'; §7.4, §7.10's Ossify and " +
        "PK_TRANSFUSION each state their own mode, and the section's default covers the rest.");

    /// <summary>
    /// 🔒 <c>REFLECT</c> — <c>FLAT</c>. `05` §4.2 routes it to <c>THORN</c>, which `05` §1 types as
    /// <em>"% of damage taken reflected"</em> — a fraction, not a multiple of anything.
    /// </summary>
    /// <remarks>
    /// This overrides §2.2's blanket <c>ATK_MULT</c> default and is recorded as errata: under it a
    /// 25% thorns grant would scale with the granter's attack power, which contradicts `05` §1's own
    /// type for the stat it writes.
    /// </remarks>
    internal static OpValueRules Reflect { get; } = new(
        ValueMode.FLAT,
        new HashSet<ValueMode> { ValueMode.FLAT },
        "05 §4.2 adds REFLECT to THORN, and 05 §1 types THORN as a fraction of damage taken.");

    /// <summary>
    /// 🔒 <c>SURVIVE_LETHAL</c> — R7. §2.4 says <em>"at a given HP fraction"</em>, so the default is
    /// <c>SELF_MAXHP_PCT</c>; §7.4's <c>PK_UNBREAKABLE</c> is <c>{"value": 1, "valueMode": "FLAT"}</c>
    /// = <b>1 HP</b>, which is what `06` writes for that perk.
    /// </summary>
    internal static OpValueRules SurviveLethal { get; } = new(
        ValueMode.SELF_MAXHP_PCT,
        new HashSet<ValueMode> { ValueMode.SELF_MAXHP_PCT, ValueMode.FLAT },
        "18 §2.4 words it as an HP fraction and 06 words PK_UNBREAKABLE as 1 HP; the valueMode key " +
        "is what lets both be authored instead of one of them being chosen.");

    /// <summary>
    /// <c>REVIVE</c> — a fraction of Max HP, and deliberately <b>not</b> widened the way
    /// <see cref="SurviveLethal"/> was.
    /// </summary>
    /// <remarks>
    /// §2.4 words it identically (<em>"at a given HP fraction"</em>), but no authored content needs a
    /// flat revive, and R7 rules on <c>SURVIVE_LETHAL</c> alone. Recorded as errata rather than
    /// closed by inventing a key nobody asked for.
    /// </remarks>
    internal static OpValueRules Revive { get; } = new(
        ValueMode.SELF_MAXHP_PCT,
        new HashSet<ValueMode> { ValueMode.SELF_MAXHP_PCT },
        "18 §2.4: 'return from 0 HP at a given HP fraction'.");
}
