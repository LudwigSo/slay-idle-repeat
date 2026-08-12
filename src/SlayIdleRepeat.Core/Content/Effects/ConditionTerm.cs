namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// One comparison in a `18` §4 condition tree: <c>{"fn": …, "op": …, "value": …}</c>.
/// </summary>
/// <remarks>
/// <para>
/// The three optional argument keys — <see cref="StatusId"/>, <see cref="Category"/> and
/// <see cref="FaceKind"/> — are what §4's table means by <em>"int, optionally by category"</em>,
/// <em>"bool, by status id"</em> and <em>"int, by face kind"</em>. `18` writes no worked example
/// carrying one, so the key <b>names</b> are reused from the names §2 and §3 already authorise for
/// the same three things rather than invented afresh.
/// </para>
/// <para>
/// ⚠️ <see cref="RangeLow"/>/<see cref="RangeHigh"/> carry
/// <see cref="ConditionComparator.BETWEEN"/>'s two bounds. `18` lists the comparator and never
/// writes one, so the two-element encoding is an assumption — a structural one, not a fabricated
/// number.
/// </para>
/// </remarks>
public sealed record ConditionTerm
{
    /// <summary>Which of `18` §4's 23 functions this term reads.</summary>
    public required ConditionFunction Fn { get; init; }

    /// <summary>How the function's result is compared.</summary>
    public required ConditionComparator Comparator { get; init; }

    /// <summary>The number compared against, for every comparator except <see cref="ConditionComparator.BETWEEN"/>.</summary>
    public double? Value { get; init; }

    /// <summary>The boolean compared against — `18` §7.10's <c>{"fn":"ATTACKER_IS_ELITE","op":"eq","value":true}</c>.</summary>
    public bool? Flag { get; init; }

    /// <summary><see cref="ConditionComparator.BETWEEN"/>'s inclusive lower bound.</summary>
    public double? RangeLow { get; init; }

    /// <summary><see cref="ConditionComparator.BETWEEN"/>'s inclusive upper bound.</summary>
    public double? RangeHigh { get; init; }

    /// <summary>The status <see cref="ConditionFunction.HAS_STATUS"/> and <see cref="ConditionFunction.STATUS_STACKS"/> read.</summary>
    public string? StatusId { get; init; }

    /// <summary>The perk category <see cref="ConditionFunction.PERK_COUNT"/> restricts to.</summary>
    public string? Category { get; init; }

    /// <summary>The die-face kind <see cref="ConditionFunction.DIE_FACE_COUNT"/> counts.</summary>
    public string? FaceKind { get; init; }
}
