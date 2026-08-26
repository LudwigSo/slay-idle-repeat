namespace SlayIdleRepeat.Application.Wire;

/// <summary>The client-generated idempotency key one command carries — 14 §2.3's <c>commandId</c>.</summary>
/// <param name="Value">The identifier text. Never null or empty, no whitespace, no control character.</param>
/// <remarks>
/// <para>
/// The guard is exactly <c>IIdGeneratorPort.NewCommandId</c>'s contract, restated on the receiving
/// side: the id travels in a URL, a log line and a header unescaped, so a value that would not
/// survive those is refused at the door rather than half-matched in a store later. Nothing parses
/// the text — it is an opaque token compared under ordinal equality, which is what a synthesized
/// <c>record struct</c> equality over one string is.
/// </para>
/// <para>
/// A distinct type rather than a string for <c>PlayerId</c>'s reason: a command id and a player id
/// meet in the same idempotency record, and nothing may pass one where the other belongs.
/// </para>
/// </remarks>
public readonly record struct CommandId(string Value)
{
    /// <summary>
    /// The identifier text. Never null, empty, whitespace-bearing or control-bearing — except on
    /// <c>default(CommandId)</c>, whose backing field no constructor ever assigned.
    /// </summary>
    public string Value { get; } = Require(Value);

    /// <summary>Whether the text is a well-formed command id, without throwing.</summary>
    /// <param name="value">The candidate text.</param>
    /// <remarks>
    /// The parser's door: an envelope carries client input, and a malformed id there is a request
    /// to refuse, not an exception to surface. One predicate serves both paths so the constructor
    /// and the parser cannot drift on what "well-formed" means.
    /// </remarks>
    public static bool IsWellFormed(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The identifier text, so a log line reads the id rather than the record's shape.</summary>
    /// <remarks>The <c>??</c> is <c>default(CommandId)</c>, on <c>PlayerId.ToString</c>'s argument.</remarks>
    public override string ToString() => Value ?? $"default({nameof(CommandId)})";

    private static string Require(string value) =>
        IsWellFormed(value)
            ? value
            : throw new ArgumentException(
                "A command id is a non-empty string with no whitespace and no control character " +
                "(14 §2.3, IIdGeneratorPort.NewCommandId). It rides a URL, a log line and a header " +
                "unescaped; a value that would not survive those cannot be an idempotency key.",
                nameof(CommandId));
}
