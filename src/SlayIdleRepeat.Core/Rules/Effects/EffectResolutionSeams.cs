using SlayIdleRepeat.Core.Content.Effects;
using SlayIdleRepeat.Core.Rules.Effects.Conditions;

namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>The condition-filter gate — evaluated against current state.</summary>
/// <remarks>
/// A caller that has the live fight passes <see cref="EffectConditionGate"/>; a caller that doesn't
/// (the balance harness over synthetic stat blocks) passes <c>UnconditionalEffectsOnly</c>, which
/// refuses a conditional effect rather than guessing which way it would have gone.
/// </remarks>
internal interface IEffectConditionGate
{
    /// <summary>True when the effect's condition holds against current state.</summary>
    bool IsActive(EffectDefinition effect);
}

/// <summary>An effect's effective value: its authored value after <c>valueScale</c> and <c>valueMode</c>.</summary>
/// <remarks>
/// Not the same seam as <c>Ops.IScaledValueReader</c>: that one applies scaling alone. This one is
/// the whole magnitude — both multiplications — since <c>STAT_SET MAX_HP</c> is the one stat op that
/// also carries a <c>valueMode</c>.
/// </remarks>
internal interface IEffectValueReader
{
    /// <summary>The effect's value after scaling and value mode are applied.</summary>
    double EffectiveValue(EffectDefinition effect);
}

/// <summary>The real condition gate: evaluates an effect's condition against one evaluation context.</summary>
/// <remarks>
/// <para>
/// One gate instance per resolution pass, holding the context and nothing else — conditions are
/// pure functions of current state, so the gate caches nothing. That matters where the gate is
/// applied twice in one pass (<c>StatAggregation</c> re-applies it to stat ops): handing both call
/// sites the same gate makes the second evaluation agree with the first by construction.
/// </para>
/// <para>
/// An <see cref="EffectContextException"/> from the evaluator is not caught here — a condition that
/// can't resolve is content that failed to skip itself (e.g. via <c>IS_PVP</c>), and swallowing it
/// would turn that into an effect silently missing from the build.
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

        // A null condition is an ungated effect and holds.
        return ConditionEvaluator.IsSatisfied(effect.Condition, _context);
    }
}
