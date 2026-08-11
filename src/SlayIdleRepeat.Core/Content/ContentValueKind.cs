namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// The kinds a <see cref="ContentValue"/> can take.
/// </summary>
/// <remarks>
/// 🔒 There is deliberately no <c>Null</c>. `SlayIdleRepeat.Data/README.md`:
/// <em>"<c>null</c> means 'the design docs do not authorise a value here.' It is never a
/// legitimate runtime value."</em> A JSON <c>null</c> therefore loads as
/// <see cref="Unauthorised"/> — a kind whose whole purpose is that no reader can mistake it
/// for zero, empty or a default. 98 leaves in the shipped tuning files are in this state.
/// </remarks>
public enum ContentValueKind
{
    /// <summary>The design docs do not authorise a value here (`SlayIdleRepeat.Data/README.md`).</summary>
    Unauthorised = 0,

    /// <summary>A JSON object. Members are held ordinal-sorted by name.</summary>
    Object = 1,

    /// <summary>A JSON array. Item order is significant and preserved.</summary>
    Array = 2,

    /// <summary>A JSON string.</summary>
    Text = 3,

    /// <summary>A JSON number, held as <see cref="decimal"/> so the load path never rounds.</summary>
    Number = 4,

    /// <summary>A JSON boolean.</summary>
    Boolean = 5,
}
