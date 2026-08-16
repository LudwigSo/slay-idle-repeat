namespace SlayIdleRepeat.Core.Model;

/// <summary>A player's lifetime feat counters: counter id to the count accumulated since the account existed.</summary>
/// <remarks>
/// <para>
/// Lifetime and additive. Nothing decreases a count and nothing resets one — an achievement is
/// claimed retroactively against it, so a reset pays out against a history the player did not have
/// and the real history cannot be recovered.
/// </para>
/// <para>
/// A live view over <c>Player</c>'s own map: read it, do not hold it across a mutation. Record
/// equality compares <see cref="Counts"/> by reference and is meaningless for it.
/// </para>
/// </remarks>
public sealed record FeatCounters
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
