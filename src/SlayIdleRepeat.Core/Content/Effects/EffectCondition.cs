namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// A node of a `18` §4 condition tree: a <see cref="ConditionTerm"/>, or one of the combinators
/// <c>all · any · not</c>.
/// </summary>
/// <remarks>
/// `18` §4: <em>"Conditions gate an effect without changing when it is evaluated. All are pure
/// functions of current state."</em> Nothing in this type evaluates anything — M2-05 does, against
/// the same tree.
/// </remarks>
public sealed record EffectCondition
{
    /// <summary>Whether this node is a term or a combinator.</summary>
    public required ConditionKind Kind { get; init; }

    /// <summary>The comparison, when <see cref="Kind"/> is <see cref="ConditionKind.TERM"/>.</summary>
    public ConditionTerm? Term { get; init; }

    /// <summary>
    /// The combinator's operands. <see cref="ConditionKind.NOT"/> carries exactly one;
    /// <see cref="ConditionKind.ALL"/> and <see cref="ConditionKind.ANY"/> carry one or more; a
    /// <see cref="ConditionKind.TERM"/> carries none.
    /// </summary>
    public IReadOnlyList<EffectCondition> Operands { get; init; } = [];

    /// <summary>A comparison node.</summary>
    public static EffectCondition Of(ConditionTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);

        return new EffectCondition { Kind = ConditionKind.TERM, Term = term };
    }

    /// <summary><c>{"all": [ … ]}</c>.</summary>
    public static EffectCondition All(params EffectCondition[] operands) =>
        Combinator(ConditionKind.ALL, operands);

    /// <summary><c>{"any": [ … ]}</c>.</summary>
    public static EffectCondition Any(params EffectCondition[] operands) =>
        Combinator(ConditionKind.ANY, operands);

    /// <summary><c>{"not": { … }}</c>.</summary>
    public static EffectCondition Not(EffectCondition operand)
    {
        ArgumentNullException.ThrowIfNull(operand);

        // `new[] { … }` rather than the collection expression `[operand]`: for a single element
        // targeting IReadOnlyList<T>, Roslyn synthesises <>z__ReadOnlySingleElementList in the
        // GLOBAL namespace without a CompilerGeneratedAttribute, and
        // AccessibilityBoundaryTests.Every_Core_type_lives_under_a_documented_namespace — which
        // filters on that attribute — reports it as an undocumented Core namespace. An array
        // allocation is the same cost and leaves no type behind.
        return new EffectCondition { Kind = ConditionKind.NOT, Operands = new[] { operand } };
    }

    private static EffectCondition Combinator(ConditionKind kind, EffectCondition[] operands)
    {
        ArgumentNullException.ThrowIfNull(operands);

        if (operands.Length == 0)
        {
            // 🔒 An empty `all` is vacuously true and an empty `any` vacuously false, so a
            // combinator that lost its operands would gate nothing while still reading as a gate.
            throw new ArgumentException(
                $"a {kind} combinator with no operands gates nothing — an empty 'all' is vacuously " +
                "true and an empty 'any' vacuously false, so the effect would fire (or never fire) " +
                "with its condition still present in the data.",
                nameof(operands));
        }

        return new EffectCondition { Kind = kind, Operands = operands };
    }
}
