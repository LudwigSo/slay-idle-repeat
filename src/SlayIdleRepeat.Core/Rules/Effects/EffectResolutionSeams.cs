using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// 🔒 `18` §8 <b>step 2</b> — <em>"filter by condition, evaluated against current state."</em>
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Relocated here by M2-02, and the relocation is R17's, not a preference.</b> M2-07 declared
/// this in <c>Rules/Stats/</c> rather than reach into a sibling's directory mid-wave, and recorded
/// that it did not belong there. `30` §11.4 assigns `18`'s concerns to <c>Rules/Effects/</c>, and R17
/// makes it mandatory: the intra-<c>Rules</c> layering is
/// <c>Rules.Combat ▶ Rules.Stats ▶ Rules.Effects</c>, so <c>Rules.Effects</c> is the <b>bottom</b> and
/// cannot be the layer borrowing an abstraction from <c>Rules.Stats</c>. Left where it was, every
/// `18` §4 consumer of the step-2 gate would have had to reach <em>up</em> a layer to name it.
/// <c>IntraRulesLayeringRuleTests</c> enforces the direction; <c>Rules.Stats</c> naming this is the
/// permitted way round.
/// </para>
/// <para>
/// Condition evaluation needs the live fight: `18` §4's functions read HP fractions, enemy counts,
/// battle time, status stacks, gold held and the <c>IS_PVP</c> flag. A caller that has the fight
/// passes <see cref="EffectConditionGate"/>; a caller that does not — the balance harness over
/// synthetic stat blocks (`05` §9) — passes <c>UnconditionalEffectsOnly</c>, which refuses a
/// conditional effect rather than guessing which way it would have gone.
/// </para>
/// </remarks>
internal interface IEffectConditionGate
{
    /// <summary>True when the effect's `18` §4 condition holds against current state.</summary>
    bool IsActive(EffectDefinition effect);
}

/// <summary>
/// 🔒 `18` §1.1 / §2.2 — an effect's <em>effective</em> value: its authored <c>value</c> after
/// <c>valueScale</c> and <c>valueMode</c>. The seam <b>M2-06</b> (and, for the stat-op subset,
/// <b>M2-03</b>) implements, and the one `18` §8's steps 4-9 read every magnitude through.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Relocated from <c>Rules/Stats/</c> by M2-02 for the reason above:
/// <see cref="EffectDefinition"/> is its whole signature, `18` §1.1 is its whole subject, and R17 puts
/// `18`'s interpreter at the bottom.
/// </para>
/// <para>
/// ⚠️ <b>Not the same seam as <c>Rules.Effects.Ops.IScaledValueReader</c>, and the split is
/// M2-03's.</b> That one is `18` §1.1 alone — <c>value × steps</c>, the first of two
/// multiplications. This one is the whole magnitude, both multiplications, because `18` §8's stat
/// steps consume a finished number and `18` §9.1's <c>STAT_SET MAX_HP</c> is the one stat op in the
/// document that carries a <c>valueMode</c> at all.
/// </para>
/// </remarks>
internal interface IEffectValueReader
{
    /// <summary>The effect's value after `18` §1.1's scaling and `18` §2.2's value mode.</summary>
    double EffectiveValue(EffectDefinition effect);
}

/// <summary>
/// 🔒 The real `18` §8 step 2 gate: M2-05's <c>ConditionEvaluator</c>, against one evaluation context.
/// </summary>
/// <remarks>
/// <para>
/// This is what M2-07's <c>UnconditionalEffectsOnly</c> said was coming — <em>"M2-05 replaces this
/// with the real evaluator"</em> — and it lands with the step-2 owner rather than with either of
/// them: M2-05 built the evaluator, M2-07 built the seam, and step 2 is M2-02's.
/// </para>
/// <para>
/// 🔒 <b>One gate instance per resolution pass, holding the context and nothing else.</b> `18` §4's
/// functions are <em>"pure functions of current state"</em> and the context is a reading assembled by
/// the caller, so the gate caches nothing and has nothing to invalidate. That matters at the one
/// place `18` §8's step 2 is asked twice: <c>StatAggregation</c> re-applies the gate to the stat ops
/// so that it is correct when called on its own, and its remarks require the second evaluation to
/// agree with the first. Handing both the <em>same</em> gate is what makes that true by construction
/// rather than by discipline — see <see cref="EffectResolver.Resolve(EffectSourceSet, EffectEvaluationContext)"/>.
/// </para>
/// <para>
/// ⚠️ An <c>EffectContextException</c> from the evaluator is <b>not</b> caught here. `18` §9.3 rules
/// that a clause with no meaning in the current context is <em>"simply skipped"</em> by an authored
/// <c>IS_PVP</c> condition, so a condition that cannot resolve is content that failed to skip itself
/// — and swallowing it would turn that into an effect silently missing from the build, which is the
/// balance bug the whole strict-seam pattern exists to avoid.
/// </para>
/// </remarks>
internal sealed class EffectConditionGate : IEffectConditionGate
{
    private readonly EffectEvaluationContext _context;

    /// <summary>A gate over one evaluation context.</summary>
    internal EffectConditionGate(EffectEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
    }

    /// <inheritdoc />
    public bool IsActive(EffectDefinition effect)
    {
        ArgumentNullException.ThrowIfNull(effect);

        // 🔒 A null condition is an ungated effect and holds — `18` §1's `"condition": null`.
        //    ConditionEvaluator owns that reading; restating it here would be two statements of one
        //    rule, and the second would be the one nobody updated.
        return ConditionEvaluator.IsSatisfied(effect.Condition, _context);
    }
}
