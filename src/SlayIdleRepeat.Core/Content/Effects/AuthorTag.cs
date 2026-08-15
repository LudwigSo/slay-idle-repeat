namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// One of a perk's free-form author labels — <c>"offense"</c>, <c>"drawback"</c> — read as a type
/// rather than as a bare string.
/// </summary>
/// <remarks>
/// <para>
/// An open set in which exactly one member is reserved: <see cref="Drawback"/> is the marker cursed
/// perks use so wards know not to silently delete perk drawbacks. Everything else an author writes
/// is a label with no engine meaning.
/// </para>
/// <para>
/// Deliberately not the same type as <see cref="StatusTag"/>, a different vocabulary that happens
/// to be spelled the same way: two strings are interchangeable at every call site; two record
/// structs are not, so <c>REMOVE_STATUS</c> can never be handed the ward-bypass marker by accident.
/// </para>
/// <para>
/// Compared ordinally, like every other id in the DSL: a culture-aware comparison would make ward
/// bypass locale-dependent.
/// </para>
/// </remarks>
/// <param name="Value">The label as authored, lower-snake per the schema's <c>tags</c> pattern.</param>
public readonly record struct AuthorTag(string Value)
{
    /// <summary>The one reserved label — the marker for the ward-bypass class of drawback.</summary>
    public static AuthorTag Drawback { get; } = new("drawback");

    /// <summary>Whether this is the reserved <see cref="Drawback"/> label.</summary>
    public bool IsDrawback => string.Equals(Value, Drawback.Value, StringComparison.Ordinal);

    /// <summary>The label as it is authored in JSON.</summary>
    public override string ToString() => Value ?? string.Empty;
}
