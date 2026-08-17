namespace SlayIdleRepeat.Core.Primitives;

/// <summary>The identity of one rolled gear item — the thing a player equips, merges or salvages.</summary>
/// <param name="Value">The identifier text. Never null, empty or whitespace.</param>
/// <remarks>
/// <para>
/// The same shape, guard and reasoning as <see cref="PlayerId"/> and <see cref="RunId"/>; see
/// <see cref="PlayerId"/>'s remarks for why it is a <c>readonly record struct</c>. What matters here
/// is that it is a <em>different</em> type from either: an equip command carries a gear instance id
/// and a player id, and nothing may pass one where the other belongs.
/// </para>
/// <para>
/// It is not the item's <c>defId</c>. A def id names one of the twenty-four base items and is shared
/// by every copy of it; this names a single rolled instance, with its own quality, chapter of origin,
/// enhancement level, mercy counter and affixes.
/// </para>
/// <para>
/// The server owns it: the rolled instance never leaves the server, so a command carries the id and
/// never the item. That is what lets <c>Commands/</c> stay beneath <c>Model/</c>.
/// </para>
/// </remarks>
public readonly record struct GearInstanceId(string Value)
{
    /// <summary>
    /// The identifier text. Never null, empty or whitespace — except on
    /// <c>default(GearInstanceId)</c>, whose backing field no constructor ever assigned.
    /// </summary>
    public string Value { get; } = IdText.Require(Value, nameof(GearInstanceId));

    /// <summary>The identifier text, so a log line reads the id rather than the record's shape.</summary>
    /// <remarks>The <c>??</c> is <c>default(GearInstanceId)</c>, for the reason spelled out on <see cref="PlayerId.ToString"/>.</remarks>
    public override string ToString() => Value ?? $"default({nameof(GearInstanceId)})";
}
