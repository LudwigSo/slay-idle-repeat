using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Values;

namespace SlayIdleRepeat.Core.Rules.Effects.Ops;

/// <summary><c>valueMode</c> — what <c>value</c> is a multiple of — applied to one op's scaled value.</summary>
/// <remarks>
/// <para>
/// Each op declares which modes it admits and what its own default is, rather than sharing one
/// global default: several ops have their own stated unit (a percentage of the target's Max HP, a %
/// of damage just dealt, a fraction rather than an ATK multiple), and applying a shared default
/// uniformly would make those scale with attack power by accident. The op's own row wins where it
/// states a unit; where neither states one, the op refuses. The whole table is in
/// <see cref="OpValueRules"/>.
/// </para>
/// <para>The scaling has already happened here — the input is the value after <c>valueScale</c>; this is only the second multiplication.</para>
/// </remarks>
internal static class OpValue
{
    /// <summary>The number an op applies: its scaled value, multiplied by whatever the mode makes it a multiple of, rounded to 4 dp.</summary>
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

        return OpRounding.Round(
            ValueModeEvaluator.Resolve(mode, scaled, SubjectsFor(context, target, mode), effect.Id),
            effect.Id,
            $"{mode} value");
    }

    /// <summary>The mode this effect resolves under — the authored one, or the op's own default.</summary>
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

    /// <summary>The subjects value modes read, gathered from this op's firing context for <see cref="ValueModeEvaluator.Resolve"/> — the one statement of the eight-mode table.</summary>
    /// <remarks>
    /// A gatherer rather than a second switch, so the eight-way mode logic lives once in the
    /// evaluator instead of being restated here and drifting from it.
    /// <para>
    /// <see cref="ValueModeSubjects.SourceAttack"/> is read only for <c>ATK_MULT</c>: the bundle is
    /// eager, and the ATK read crosses a resolved-stat seam, so gathering it for every mode
    /// regardless would cost every op a seam call no mode needs.
    /// </para>
    /// </remarks>
    private static ValueModeSubjects SubjectsFor(
        EffectOpContext context, IEffectActorView? target, ValueMode mode) =>
        new()
        {
            // The source's ATK is the holder's, never the target's, read through the snapshot seam
            // since Rules.Stats isn't reachable from here.
            SourceAttack = mode == ValueMode.ATK_MULT
                ? context.Seams.Stats.FinalStat(context.Holder, StatId.ATK)
                : null,
            Source = context.Holder,
            Target = target,
            DamageDealt = context.DamageDealt,
            HealAmount = context.HealAmount,
            OverhealAmount = context.OverhealAmount,
        };
}

/// <summary>Which value modes one op admits, and which it falls back to. One instance per op, in <see cref="OpValueRules"/>'s static members.</summary>
/// <param name="Default">The mode used when the effect authors none.</param>
/// <param name="Admits">Every mode the op admits, including <paramref name="Default"/>.</param>
/// <param name="Reason">The clause the set and the default are read from.</param>
internal sealed record OpValueRules(
    ValueMode Default, IReadOnlySet<ValueMode> Admits, string Reason)
{
    /// <summary>Every mode that produces an HP-shaped amount from a source, a target or an event.</summary>
    private static readonly IReadOnlySet<ValueMode> AmountModes = new HashSet<ValueMode>
    {
        ValueMode.ATK_MULT, ValueMode.FLAT, ValueMode.SELF_MAXHP_PCT, ValueMode.TARGET_MAXHP_PCT,
        ValueMode.TARGET_MISSING_HP_PCT, ValueMode.DAMAGE_DEALT_PCT,
    };

    /// <summary>The same, plus the two readings that exist only inside <c>ON_HEAL</c>.</summary>
    private static readonly IReadOnlySet<ValueMode> AmountOrHealReadingModes =
        new HashSet<ValueMode>(AmountModes)
        {
            ValueMode.HEAL_AMOUNT, ValueMode.OVERHEAL_AMOUNT,
        };

    /// <summary><c>DAMAGE</c> — the value IS the AttackMultiplier itself; the op multiplies by nothing here.</summary>
    /// <remarks>
    /// <para>
    /// Validation only — <see cref="OpValue.Amount"/> is never called with this. The scaled value is
    /// passed straight to <see cref="IAttackPipeline.ResolveAttack"/>, which multiplies by the
    /// attacker's ATK; running it through <see cref="OpValue.Amount"/> too would apply ATK twice.
    /// </para>
    /// <para>
    /// Every other mode is refused: turning a flat value into a multiplier by dividing by ATK would
    /// make the same effect hit differently across builds for no documented reason.
    /// </para>
    /// </remarks>
    internal static OpValueRules Damage { get; } = new(
        // Not the section's general default — DAMAGE's value being the AttackMultiplier is a
        // narrower rule, so ATK_MULT is the only admitted mode here rather than an override-able one.
        ValueMode.ATK_MULT,
        new HashSet<ValueMode> { ValueMode.ATK_MULT },
        "05 §4.2 makes DAMAGE's value the AttackMultiplier itself — ResolveAttack applies " +
        "attacker.ATK, so the op must not.");

    /// <summary><c>DAMAGE_TRUE</c> — an HP amount, on the section's default mode. HP is reduced directly.</summary>
    internal static OpValueRules DamageTrue { get; } = new(
        ValueModeEvaluator.DamageAndHealingDefault, AmountModes,
        "18 §2.2: 'value is a multiple of the source's ATK unless valueMode says otherwise', and " +
        "05 §4.2 reduces HP by it directly.");

    /// <summary><c>DAMAGE_MAXHP_PCT</c> — <c>TARGET_MAXHP_PCT</c>, a percentage of the target's Max HP, from the op's own row rather than the section default.</summary>
    /// <remarks>Both authored users agree, and neither would work under the global ATK_MULT default.</remarks>
    internal static OpValueRules DamageMaxHpPct { get; } = new(
        ValueMode.TARGET_MAXHP_PCT,
        new HashSet<ValueMode> { ValueMode.TARGET_MAXHP_PCT, ValueMode.SELF_MAXHP_PCT },
        "18 §2.2's own row for this op names the unit — a percentage of the target's Max HP — and " +
        "both authored users (18 §7.10's Volatile, §7.5's CP_BLOOD_PRICE) read that way.");

    /// <summary><c>HEAL</c> — an HP amount; the section's default applies, and healing is scaled by HEAL%.</summary>
    internal static OpValueRules Heal { get; } = new(
        ValueModeEvaluator.DamageAndHealingDefault, AmountOrHealReadingModes,
        "18 §2.2: 'heal a flat amount or a % of Max HP', with the section's stated ATK_MULT default " +
        "where the author writes neither.");

    /// <summary><c>HEAL_LEECH</c> — <c>DAMAGE_DEALT_PCT</c>, and nothing else. The op's own row IS the mode: a % of damage just dealt.</summary>
    internal static OpValueRules HealLeech { get; } = new(
        ValueMode.DAMAGE_DEALT_PCT,
        new HashSet<ValueMode> { ValueMode.DAMAGE_DEALT_PCT },
        "18 §2.2's row for this op states the basis outright — 'a % of damage just dealt' — so no " +
        "other mode has a meaning here.");

    /// <summary><c>SHIELD</c> — a ward amount. Every authored user writes its mode explicitly.</summary>
    internal static OpValueRules Shield { get; } = new(
        ValueModeEvaluator.DamageAndHealingDefault, AmountOrHealReadingModes,
        "18 §2.2's SHIELD grants 'a WARD absorb of a given size'; §7.4, §7.10's Ossify and " +
        "PK_TRANSFUSION each state their own mode, and the section's default covers the rest.");

    /// <summary><c>REFLECT</c> — FLAT. Routes to THORN, typed as a fraction of damage taken reflected, not a multiple of anything.</summary>
    /// <remarks>Overrides the section's blanket ATK_MULT default — under it a 25% thorns grant would scale with the granter's attack power.</remarks>
    internal static OpValueRules Reflect { get; } = new(
        ValueMode.FLAT,
        new HashSet<ValueMode> { ValueMode.FLAT },
        "05 §4.2 adds REFLECT to THORN, and 05 §1 types THORN as a fraction of damage taken.");

    /// <summary><c>SURVIVE_LETHAL</c> — default a fraction of Max HP, but also admits FLAT for an exact HP value (e.g. "survive at 1 HP").</summary>
    internal static OpValueRules SurviveLethal { get; } = new(
        ValueMode.SELF_MAXHP_PCT,
        new HashSet<ValueMode> { ValueMode.SELF_MAXHP_PCT, ValueMode.FLAT },
        "18 §2.4 words it as an HP fraction and 06 words PK_UNBREAKABLE as 1 HP; the valueMode key " +
        "is what lets both be authored instead of one of them being chosen.");

    /// <summary><c>REVIVE</c> — a fraction of Max HP only, deliberately not widened the way <see cref="SurviveLethal"/> was — no authored content needs a flat revive.</summary>
    internal static OpValueRules Revive { get; } = new(
        ValueMode.SELF_MAXHP_PCT,
        new HashSet<ValueMode> { ValueMode.SELF_MAXHP_PCT },
        "18 §2.4: 'return from 0 HP at a given HP fraction'.");
}
