namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// The "tag group" — a label on a status, which <see cref="EffectOp.REMOVE_STATUS"/> clears in bulk.
/// </summary>
/// <remarks>
/// <para>
/// A different type from <see cref="AuthorTag"/>, and that is the point: that is an effect label
/// set in which <c>drawback</c> is reserved for ward bypass; this is a status label set with no
/// reserved member and no catalogue yet. The two vocabularies are spelled identically — a
/// lower-snake string — so nothing but the type keeps them apart.
/// </para>
/// <para>
/// No member is declared, deliberately: naming a tag here would invent a status taxonomy nobody
/// has agreed. The engine's obligation is to clear whichever statuses carry the authored label;
/// which labels exist is a status catalogue's to author.
/// </para>
/// <para>Compared ordinally, for the reason <see cref="EffectOrder"/> records.</para>
/// </remarks>
/// <param name="Value">The status label as authored.</param>
public readonly record struct StatusTag(string Value)
{
    /// <summary>The label as it is authored in JSON.</summary>
    public override string ToString() => Value ?? string.Empty;
}
