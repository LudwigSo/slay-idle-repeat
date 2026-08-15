using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Conditions;

/// <summary>
/// The three optional argument keys a condition function may carry: a status id, an optional
/// category, and a face kind.
/// </summary>
/// <remarks>
/// Exists as its own type, rather than the evaluator taking a <see cref="ConditionTerm"/> directly,
/// because <see cref="ValueScale"/> reuses the same condition functions and needs the same three
/// keys read the same way, so a function can't answer a scale differently from a condition.
/// </remarks>
/// <param name="StatusId">The status <c>HAS_STATUS</c> and <c>STATUS_STACKS</c> read.</param>
/// <param name="Category">The perk category <c>PERK_COUNT</c> restricts to.</param>
/// <param name="FaceKind">The face kind <c>DIE_FACE_COUNT</c> counts.</param>
internal readonly record struct ConditionArguments(string? StatusId, string? Category, string? FaceKind)
{
    /// <summary>No arguments — every function that needs one then fails loudly rather than guessing.</summary>
    internal static ConditionArguments None => default;

    /// <summary>The arguments carried by a condition term.</summary>
    /// <exception cref="ArgumentNullException">The term is null.</exception>
    internal static ConditionArguments Of(ConditionTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);

        return new ConditionArguments(term.StatusId, term.Category, term.FaceKind);
    }

    /// <summary>The arguments carried by a <c>valueScale</c>.</summary>
    /// <exception cref="ArgumentNullException">The scale is null.</exception>
    internal static ConditionArguments Of(ValueScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);

        return new ConditionArguments(scale.StatusId, scale.Category, scale.FaceKind);
    }
}
