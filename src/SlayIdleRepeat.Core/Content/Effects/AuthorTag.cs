namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>
/// 🔒 One of `18` §1's free-form author labels — <c>"offense"</c>, <c>"drawback"</c> — read as a
/// type rather than as a bare string.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>An open set in which exactly one member is reserved.</b> `18` §1 gives <c>tags</c> no
/// vocabulary at all, and `18` §7.5 authors <c>CP_BLOOD_PRICE</c> with <c>"tags": ["drawback"]</c>.
/// `05` §4.1's ward <b>bypass list</b> (b) then names the same idea in prose without naming a
/// marker: <em>"self-inflicted costs (cursed-perk drawbacks such as <c>CP_BLOOD_PRICE</c> /
/// <c>CP_TIMEBOUND</c>)"</em> — <em>"wards must not silently delete perk drawbacks"</em>. The marker
/// is defined by that one example, so <see cref="Drawback"/> is reserved here and M2-09 keys ward
/// bypass on it. Everything else an author writes is a label with no engine meaning.
/// </para>
/// <para>
/// 🔒 <b>Deliberately NOT the same type as <see cref="StatusTag"/>.</b> `18` §2.3 says
/// <see cref="EffectOp.REMOVE_STATUS"/> clears <em>"a status or a tag group"</em>, and that tag
/// group is a label on a <b>status</b> — a different vocabulary that happens to be spelled the same
/// way. Two strings are interchangeable at every call site; two record structs are not, so
/// <c>REMOVE_STATUS</c> can never be handed the ward-bypass marker by accident and no future
/// ward-bypass check can be satisfied by a status label. That separation is the whole reason this
/// type exists — it buys nothing else over <see cref="string"/>.
/// </para>
/// <para>
/// Compared <b>ordinally</b>, like every other id in the DSL (<see cref="EffectOrder"/>): a
/// culture-aware comparison would make ward bypass locale-dependent.
/// </para>
/// </remarks>
/// <param name="Value">The label as authored, lower-snake per the schema's <c>tags</c> pattern.</param>
public readonly record struct AuthorTag(string Value)
{
    /// <summary>
    /// 🔒 The one reserved label — `18` §7.5's marker for `05` §4.1's ward-bypass class (b).
    /// </summary>
    public static AuthorTag Drawback { get; } = new("drawback");

    /// <summary>Whether this is the reserved <see cref="Drawback"/> label.</summary>
    public bool IsDrawback => string.Equals(Value, Drawback.Value, StringComparison.Ordinal);

    /// <summary>The label as it is authored in JSON.</summary>
    public override string ToString() => Value ?? string.Empty;
}
