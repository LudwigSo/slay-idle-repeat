namespace SlayIdleRepeat.Core.Model;

/// <summary>A player's lifetime feat counters: counter id to the count accumulated since the account existed.</summary>
/// <remarks>
/// <para>
/// Lifetime and additive. Nothing decreases a count and nothing resets one — an achievement is
/// claimed retroactively against it, so a reset pays out against a history the player did not have
/// and the real history cannot be recovered.
/// </para>
/// <para>
/// A live view over <c>Player</c>'s own map: read it, do not hold it across a mutation.
/// </para>
/// <para>
/// 🔒 <b>A <c>class</c>, not a <c>record</c>, and that is steering S17 rather than a style choice.</b>
/// A synthesized record <c>Equals</c> compares an <c>IReadOnlyDictionary</c> component <b>by
/// reference</b>, so two views over equal maps would have compared unequal and two views over one map
/// would have compared equal whatever it held — an equality that means nothing while looking like it
/// means something, which is the shape M1's two Criticals had. This type used none of what a record
/// buys: no positional syntax, no <c>with</c>, an explicit constructor and one get-only property. So
/// the meaningless equality is <em>deleted</em> rather than documented, and identity comparison —
/// which is what a live view actually supports — is what is left. Aggregate state is compared by
/// `14` §16.6's canonical bytes, never by a value comparison on a view.
/// </para>
/// </remarks>
public sealed class FeatCounters
{
    /// <summary>Builds a view over <paramref name="counts"/>.</summary>
    internal FeatCounters(IReadOnlyDictionary<string, long> counts) => Counts = counts;

    /// <summary>Counter id → lifetime count. A counter absent from this map has never been advanced.</summary>
    public IReadOnlyDictionary<string, long> Counts { get; }

    /// <summary>The lifetime count for <paramref name="counterId"/>, or zero when nothing has advanced it.</summary>
    /// <remarks>
    /// Zero for a blank id rather than throwing, unlike <c>Player.FeatCount</c>: this is a view over
    /// a map, and asking it about a key it does not hold has an answer.
    /// </remarks>
    public long CountOf(string counterId) =>
        !string.IsNullOrWhiteSpace(counterId) && Counts.TryGetValue(counterId, out var count) ? count : 0L;
}
