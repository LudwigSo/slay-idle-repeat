namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>The identity one stored battle log lives under.</summary>
/// <param name="Value">The identifier text. Non-empty, and only <c>A-Z a-z 0-9 . _ -</c>.</param>
/// <remarks>
/// <para>
/// The same closed ordinal spelling every stored identity in this repository uses, refused at
/// construction rather than escaped in a store: two different ids that mangled to one storage name
/// would read each other's log, and an id one backing accepts and another rejects would make the
/// two implementations disagree about which logs exist.
/// </para>
/// <para>
/// Deliberately says nothing about where or how a log is stored — the id is the port's whole
/// address vocabulary, so every backing derives its own internal name from this text alone.
/// </para>
/// </remarks>
public readonly record struct BattleLogId(string Value)
{
    /// <summary>The identifier text. Never null, empty, or outside the closed spelling — except on <c>default(BattleLogId)</c>, whose backing field no constructor assigned.</summary>
    public string Value { get; } = Require(Value);

    /// <summary>The identifier text, so a log line reads the id rather than the record's shape.</summary>
    public override string ToString() => Value ?? $"default({nameof(BattleLogId)})";

    private static string Require(string value)
    {
        if (!string.IsNullOrEmpty(value) && AllInsideSpelling(value))
        {
            return value;
        }

        throw new ArgumentException(
            "A battle-log id is non-empty text made only of A-Z, a-z, 0-9, '.', '_' and '-'. " +
            "'" + (value ?? "<no text at all>") + "' is outside that spelling; escaping it in a store " +
            "would let two different ids collide on one stored name.",
            nameof(Value));
    }

    private static bool AllInsideSpelling(string value)
    {
        foreach (var character in value)
        {
            var inside = character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                or '.' or '_' or '-';

            if (!inside)
            {
                return false;
            }
        }

        return true;
    }
}
