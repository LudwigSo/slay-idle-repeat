namespace SlayIdleRepeat.Core.Rules.Effects.Triggers;

/// <summary>
/// 🔒 The identity of one <b>effect instance</b> — what `18` §3's <c>everyNth</c> counters live on,
/// and the key M3 persists a run-scoped <c>ON_KILL</c> counter against.
/// </summary>
/// <param name="Value">
/// The caller's stable, unique name for this instance. Compared <b>ordinally</b>, as every id in
/// this repository (`18` §8, `14` §8.2).
/// </param>
/// <remarks>
/// <para>
/// `18` §3: <em>"<c>everyNth</c> counters live on the <b>effect instance</b>: <c>ON_ATTACK</c>
/// counters reset at battle start; <c>ON_KILL</c> counters persist across battles for the run
/// (<c>PK_MIDAS</c>'s 'every 6th enemy killed')."</em> Three separate facts sit in that sentence, and
/// the first is this type: <b>per instance — not per actor, and not global.</b> Two copies of one
/// effect count separately.
/// </para>
/// <para>
/// 🔒 <b>Why the caller names it and this layer does not derive it.</b> The obvious derivation is
/// <c>(actorId, effectId)</c>, and it is wrong twice over. It collapses two copies of one effect on
/// one actor into a single counter, which is the exact property `18` §3 states. And it cannot be
/// stable across a battle boundary, which the same sentence requires of <c>ON_KILL</c>: the actor is
/// a battle-local construction, so <c>PK_MIDAS</c>'s counter would restart on the hero's next fight
/// and the perk would never fire. What is stable across a run is the <b>holding</b> — the perk in a
/// draft slot, the affix on a gear item — and only the run layer knows it. So this is a string the
/// caller supplies and this layer never parses.
/// </para>
/// <para>
/// 🔒 <b>The uniqueness is enforced, not assumed.</b> <see cref="TriggerRegistry.Register"/> refuses
/// a duplicate id within one battle. A caller that keyed on the effect id alone while holding two
/// copies would otherwise get one shared counter and a <c>PK_FLURRY</c> that fires on every 5th
/// attack instead of every 5th per copy — a defect that is invisible in every log, because both
/// spellings produce a legal-looking fight.
/// </para>
/// <para>
/// ⚠️ It is a <c>record struct</c> over a string rather than a bare <c>string</c> so that the
/// run-counter seam cannot be handed an effect id, an actor id or a status id by mistake; those are
/// all strings too, and three of the four are wrong.
/// </para>
/// </remarks>
internal readonly record struct EffectInstanceId(string Value)
{
    /// <summary>🔒 The one comparer for instance ids. Ordinal, for <c>EffectOrder</c>'s reason.</summary>
    internal static StringComparer Comparer => StringComparer.Ordinal;

    /// <summary>Builds an id, refusing the two spellings that are never a holding.</summary>
    /// <param name="value">The caller's stable name for the instance.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null, empty or whitespace.</exception>
    /// <remarks>
    /// An empty id is refused rather than admitted as "the anonymous instance": every instance
    /// registered without a name would share one counter, which is the per-instance rule failing
    /// silently in the direction that looks like it works.
    /// </remarks>
    internal static EffectInstanceId Of(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new EffectInstanceId(value);
    }

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Ordinal equality — the whole reason this type exists rather than a bare string.</summary>
    /// <param name="other">The id to compare with.</param>
    public bool Equals(EffectInstanceId other) => Comparer.Equals(Value, other.Value);

    /// <inheritdoc />
    public override int GetHashCode() => Value is null ? 0 : Comparer.GetHashCode(Value);
}
