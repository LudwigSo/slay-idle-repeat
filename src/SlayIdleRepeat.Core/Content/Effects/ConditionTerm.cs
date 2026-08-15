namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// One comparison in a condition tree: <c>{"fn": …, "op": …, "value": …}</c>.
/// </summary>
/// <remarks>
/// The three optional argument keys — <see cref="StatusId"/>, <see cref="Category"/> and
/// <see cref="FaceKind"/> — reuse names already authorised elsewhere for the same three things
/// rather than inventing new ones. <see cref="RangeLow"/>/<see cref="RangeHigh"/> carry
/// <see cref="ConditionComparator.BETWEEN"/>'s two bounds — a structural assumption about the
/// two-element encoding, not a fabricated number.
/// </remarks>
public sealed record ConditionTerm
{
    /// <summary>Which of the 23 functions this term reads.</summary>
    public required ConditionFunction Fn { get; init; }

    /// <summary>How the function's result is compared.</summary>
    public required ConditionComparator Comparator { get; init; }

    /// <summary>The number compared against, for every comparator except <see cref="ConditionComparator.BETWEEN"/>.</summary>
    public double? Value { get; init; }

    /// <summary>The boolean compared against, e.g. <c>{"fn":"ATTACKER_IS_ELITE","op":"eq","value":true}</c>.</summary>
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
