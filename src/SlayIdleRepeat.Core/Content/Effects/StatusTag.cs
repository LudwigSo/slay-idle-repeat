namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// 🔒 `18` §2.3's <em>"tag group"</em> — a label on a <b>status</b>, which
/// <see cref="EffectOp.REMOVE_STATUS"/> clears in bulk.
/// </summary>
/// <remarks>
/// <para>
/// `18` §2.3 gives <see cref="EffectOp.REMOVE_STATUS"/> as <em>"clear a status or a tag group"</em>
/// and names no key for the second form — the op could name a <c>statusId</c> and nothing else. The
/// key is added here under `18` §10's extension procedure, as <c>statusTag</c>.
/// </para>
/// <para>
/// 🔒 <b>A different type from <see cref="AuthorTag"/>, and that is the point.</b> `18` §1's
/// <c>tags</c> array is an <em>effect</em> label set in which <c>drawback</c> is reserved for
/// `05` §4.1's ward bypass; this is a <em>status</em> label set with no reserved member and no
/// catalogue yet (`05` §5 fixes twelve statuses and gives none of them a tag). The two vocabularies
/// are spelled identically — a lower-snake string — so nothing but the type keeps them apart, and
/// confusing them would let a ward-bypass marker be cleared as a status group or a status group
/// satisfy a ward-bypass check.
/// </para>
/// <para>
/// ⚠️ <b>No member is declared, deliberately.</b> Naming a tag here would invent a status taxonomy
/// nobody has agreed (steering S6). The engine's obligation is to clear whichever statuses carry
/// the authored label; which labels exist is M2-10's status catalogue to author.
/// </para>
/// <para>Compared <b>ordinally</b>, for the reason <see cref="EffectOrder"/> records.</para>
/// </remarks>
/// <param name="Value">The status label as authored.</param>
public readonly record struct StatusTag(string Value)
{
    /// <summary>The label as it is authored in JSON.</summary>
    public override string ToString() => Value ?? string.Empty;
}
