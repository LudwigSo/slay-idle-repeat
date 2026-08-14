using SlayIdleRepeat.Core.Content.Effects;

namespace SlayIdleRepeat.Core.Rules.Effects.Conditions;

/// <summary>
/// The three optional argument keys a `18` §4 function may carry — <em>"bool, by status id"</em>,
/// <em>"int, optionally by category"</em>, <em>"int, by face kind"</em>.
/// </summary>
/// <remarks>
/// <para>
/// It exists as its own type, rather than the evaluator simply taking a <see cref="ConditionTerm"/>,
/// because `18` §1.1 puts the same twenty-three functions behind <c>valueScale</c>:
/// <em>"<c>fn</c>: any condition function from §4"</em>, and its worked list names
/// <c>STATUS_STACKS</c> and <c>DIE_FACE_COUNT</c> — both of which need an argument.
/// </para>
/// <para>
/// 🔴 <b>The gap M2-05 recorded here is now closed, through this seam and not around it.</b> When
/// this type was written, <see cref="ValueScale"/> carried <c>fn</c>, <c>per</c> and <c>cap</c> and
/// no argument key — because `18` §1.1's table declared no fourth field — so a scale over
/// <c>STATUS_STACKS</c> or <c>DIE_FACE_COUNT</c> was unexpressible although §1.1 named both. M2-06
/// closed it by `18` §10's route, giving <see cref="ValueScale"/> the <em>same</em> three optional
/// keys a <see cref="ConditionTerm"/> carries and adding <see cref="Of(ValueScale)"/> below.
/// <b>This type stayed the one argument type</b>, which is what stops a function answering a scale
/// differently from a condition. Erratum recorded against `18` §1.1.
/// </para>
/// </remarks>
/// <param name="StatusId">The `05` §5 status <c>HAS_STATUS</c> and <c>STATUS_STACKS</c> read.</param>
/// <param name="Category">The `06` §2 perk category <c>PERK_COUNT</c> restricts to.</param>
/// <param name="FaceKind">The `04` §1 face kind <c>DIE_FACE_COUNT</c> counts.</param>
internal readonly record struct ConditionArguments(string? StatusId, string? Category, string? FaceKind)
{
    /// <summary>No arguments — every function that needs one then fails loudly rather than guessing.</summary>
    internal static ConditionArguments None => default;

    /// <summary>The arguments carried by a `18` §4 condition term.</summary>
    /// <exception cref="ArgumentNullException">The term is null.</exception>
    internal static ConditionArguments Of(ConditionTerm term)
    {
        ArgumentNullException.ThrowIfNull(term);

        return new ConditionArguments(term.StatusId, term.Category, term.FaceKind);
    }

    /// <summary>The arguments carried by a `18` §1.1 <c>valueScale</c>.</summary>
    /// <remarks>
    /// 🔒 The counterpart of <see cref="Of(ConditionTerm)"/>, and deliberately its mirror image: the
    /// three keys are read the same way from both, so <c>STATUS_STACKS</c> cannot mean one thing to a
    /// condition and another to a scale.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The scale is null.</exception>
    internal static ConditionArguments Of(ValueScale scale)
    {
        ArgumentNullException.ThrowIfNull(scale);

        return new ConditionArguments(scale.StatusId, scale.Category, scale.FaceKind);
    }
}
