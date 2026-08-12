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
/// ⚠️ <b><see cref="ValueScale"/> has nowhere to put one.</b> It carries <c>fn</c>, <c>per</c> and
/// <c>cap</c> and no argument key, because `18` §1.1's table declares no fourth field and M2-01
/// declared no more than the document authorises. So a scale over <c>STATUS_STACKS</c> is
/// unexpressible today. That is a genuine gap in `18` §1.1 rather than something to paper over here
/// (steering S6), and it is recorded as an erratum. This type is the seam that closes it without a
/// rewrite: M2-06 passes <see cref="None"/> today, and whatever `18` §1.1 grows to carry tomorrow.
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
}
