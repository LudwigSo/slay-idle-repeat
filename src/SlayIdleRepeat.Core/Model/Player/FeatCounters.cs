namespace SlayIdleRepeat.Core.Model;

/// <summary>A player's lifetime feat counters: counter id to the count accumulated since the account existed.</summary>
/// <remarks>
/// <para>
/// The counters are <b>lifetime and additive</b>. They never decrease and never reset — not on
/// chapter change, tier change, season roll, app update, subscription lapse or logout — for the
/// same reason pity counters do not: a Feat is claimed retroactively against the count, so a
/// counter that was ever reset silently understates a player's history and the claim it pays out
/// is simply wrong. That is also why they are aggregate state written inside <c>GameRules.Apply</c>
/// rather than a projection rebuilt from a retained event log.
/// </para>
/// <para>
/// ⚠️ <b>The counter-id vocabulary is NOT settled here.</b> The Feats feature ships much later; the
/// counters exist early only so a lifetime count predates the feature that reads it. The ids
/// currently written are exactly the ones the domain events that exist today can support, they are
/// <c>internal</c> to <c>Core</c>, and this map is deliberately open — a <c>string</c> key, no
/// closed enum — so the milestone that decides what each Feat measures is not pre-empted by an
/// id set invented before it.
/// </para>
/// <para>
/// A live view over <c>Player</c>'s own counter map, on <see cref="DraftedPerks"/>' precedent:
/// read it, do not hold it across a mutation. Record equality compares <see cref="Counts"/> by
/// reference and is meaningless for it; two players' counters are compared as canonical bytes.
/// </para>
/// </remarks>
public sealed record FeatCounters
{
    /// <summary>Builds a view over <paramref name="counts"/>. <c>internal</c>: constructible only from inside <c>Core</c>.</summary>
    internal FeatCounters(IReadOnlyDictionary<string, long> counts) => Counts = counts;

    /// <summary>Counter id → lifetime count. A counter absent from this map has never been advanced.</summary>
    public IReadOnlyDictionary<string, long> Counts { get; }

    /// <summary>The lifetime count for <paramref name="counterId"/>, or zero when nothing has advanced it.</summary>
    /// <remarks>
    /// Zero for a blank id rather than throwing: this is the read side of an open key space, and a
    /// caller asking about a counter nobody registered is asking a legitimate question. The write
    /// side refuses a blank key, which is where the id is actually decided.
    /// </remarks>
    public long CountOf(string counterId) =>
        !string.IsNullOrWhiteSpace(counterId) && Counts.TryGetValue(counterId, out var count) ? count : 0L;
}
