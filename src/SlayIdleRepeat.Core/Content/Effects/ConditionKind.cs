namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// What a node of a condition tree is: a comparison, or one of the three combinators
/// <c>all · any · not</c>.
/// </summary>
/// <remarks>
/// Like <see cref="ConditionComparator"/>, the combinators' JSON spelling is lower-case — a
/// combinator node is written <c>{"all": [ … ]}</c>, with the combinator as the member name rather
/// than as a value. <see cref="TERM"/> has no JSON token at all: a node with <c>fn</c> is a term.
/// </remarks>
public enum ConditionKind
{
    /// <summary>A comparison: <c>{"fn": …, "op": …, "value": …}</c>.</summary>
    TERM = 1,

    /// <summary><c>{"all": [ … ]}</c> — every operand holds.</summary>
    ALL = 2,

    /// <summary><c>{"any": [ … ]}</c> — at least one operand holds.</summary>
    ANY = 3,

    /// <summary><c>{"not": { … }}</c> — the single operand does not hold.</summary>
    NOT = 4,
}
