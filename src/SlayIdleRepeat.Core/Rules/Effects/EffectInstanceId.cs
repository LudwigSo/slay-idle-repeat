namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>
/// The identity of one effect instance — what <c>everyNth</c> counters live on, and the key a
/// run-scoped <c>ON_KILL</c> counter persists against.
/// </summary>
/// <param name="Value">The caller's stable, unique name for this instance. Compared ordinally.</param>
/// <remarks>
/// <para>
/// Per instance, not per actor and not global — two copies of one effect count separately. The
/// obvious derivation, <c>(actorId, effectId)</c>, is wrong twice over: it would collapse two copies
/// on one actor into one counter, and it can't survive a battle boundary since the actor is a
/// battle-local construction. What's stable across a run is the holding (a draft slot, a gear
/// affix), which only the run layer knows — so this is a string the caller supplies, never derived.
/// </para>
/// <para>
/// Uniqueness is enforced by <see cref="TriggerRegistry.Register"/>, which refuses a duplicate id
/// within one battle; two copies of an effect sharing one counter would otherwise be invisible in
/// any log.
/// </para>
/// <para>
/// A <c>record struct</c> over a string rather than a bare string, so the run-counter seam can't be
/// handed an effect id, actor id or status id by mistake. Its positional constructor still bypasses
/// <see cref="Of"/>'s validation, though — <c>default(EffectInstanceId)</c> carries a null value, so
/// consumers that store against this key re-check via <see cref="NamesAHolding"/> rather than
/// trusting construction alone.
/// </para>
/// </remarks>
internal readonly record struct EffectInstanceId(string Value)
{
    /// <summary>The one comparer for instance ids. Ordinal.</summary>
    internal static StringComparer Comparer => StringComparer.Ordinal;

    /// <summary>Builds an id, refusing the two spellings that are never a holding.</summary>
    /// <param name="value">The caller's stable name for the instance.</param>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null, empty or whitespace.</exception>
    internal static EffectInstanceId Of(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new EffectInstanceId(value);
    }

    /// <summary>Whether this id names a holding at all — false for <c>default</c> and a blank value.</summary>
    internal bool NamesAHolding => !string.IsNullOrWhiteSpace(Value);

    /// <inheritdoc />
    public override string ToString() => Value;

    /// <summary>Ordinal equality.</summary>
    public bool Equals(EffectInstanceId other) => Comparer.Equals(Value, other.Value);

    /// <inheritdoc />
    public override int GetHashCode() => Value is null ? 0 : Comparer.GetHashCode(Value);
}
