namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The identity of one inbox message.</summary>
/// <param name="Value">The identifier text. Never null, empty or whitespace.</param>
/// <remarks>
/// <para>
/// A distinct type rather than a bare <c>string</c>, for <see cref="PlayerId"/>'s reason and for one
/// more that is specific to this id: every attachment grant in the game is idempotent <em>on this
/// value</em>, so a message id handed to something expecting a player id would silently address the
/// wrong row of the one table where a mistake pays a reward twice.
/// </para>
/// <para>
/// A <c>readonly record struct</c> for <see cref="PlayerId"/>'s reasons, with the same capital in
/// <c>Value</c>. It is not carried by any snapshot today — the inbox is a read-only projection into
/// <c>WorldSlice</c> rather than player state — but the shape costs nothing and a divergent one
/// would have to be corrected the day it is.
/// </para>
/// <para>
/// <c>default(MessageId)</c> bypasses the constructor and holds a null <see cref="Value"/>; the
/// repository is the seam that validates a stored id, not this type.
/// </para>
/// </remarks>
public readonly record struct MessageId(string Value)
{
    /// <summary>
    /// The identifier text. Never null, empty or whitespace — except on
    /// <c>default(MessageId)</c>, whose backing field no constructor ever assigned.
    /// </summary>
    public string Value { get; } = IdText.Require(Value, nameof(MessageId));

    /// <summary>The identifier text, so a log line reads the id rather than the record's shape.</summary>
    public override string ToString() => Value ?? $"default({nameof(MessageId)})";
}
