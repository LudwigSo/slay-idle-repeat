using System.Collections.ObjectModel;

namespace SlayIdleRepeat.Core.Model;

/// <summary>
/// A player's pity counters: how many draws each guarantee has gone unsatisfied, keyed by the
/// content-derived counter id.
/// </summary>
/// <remarks>
/// <para>
/// Lives in <c>SlayIdleRepeat.Core.Model</c> rather than under a <c>Model.Pity</c> child namespace,
/// and holds nothing but a string-keyed map — the same shape <c>Player</c> already uses for its
/// daily counters and its cleared chapter tiers, which is what a persisted counter map has to be
/// when the key space is authored rather than declared.
/// </para>
/// <para>
/// <b>Not yet a <c>Player</c> field, deliberately.</b> The luck service is stateless: it takes
/// counters as an argument and returns the deltas, so the argument type is what has to exist first.
/// The first <em>writer</em> of a persisted counter is the container-shelf task (<b>M4-02</b>) —
/// chest ladders and the in-run drop mercy — and a field on the aggregate that nothing writes is a
/// public shape with no producer. M4-02 adds the field, the snapshot column and the
/// <c>SchemaVersion</c> bump together; <c>Player</c>'s own remarks carry the same note.
/// </para>
/// <para>
/// Immutable, and replaced wholesale rather than mutated: a resolution returns the counters it
/// would leave behind, and the caller decides whether to keep them. Nothing here decays a counter
/// or reads a clock — a counter moves only when a draw of its own class happens, and resets only
/// when its own guarantee fires.
/// </para>
/// <para>
/// Every comparison is <see cref="StringComparer.Ordinal"/>. The keys are authored ids, not display
/// text, and a culture-sensitive map would let <c>chest.standard:A</c> and a differently-cased
/// twin address the same counter on one device and two counters on another.
/// </para>
/// </remarks>
internal sealed class PityCounters
{
    /// <summary>The value a counter reads when it has never advanced, or has just been reset.</summary>
    internal const int Unstarted = 0;

    private readonly ReadOnlyDictionary<string, int> _view;

    private PityCounters(Dictionary<string, int> counters) =>
        _view = new ReadOnlyDictionary<string, int>(counters);

    /// <summary>A player who has never drawn anything. Every counter reads <see cref="Unstarted"/>.</summary>
    internal static PityCounters Empty { get; } =
        new(new Dictionary<string, int>(StringComparer.Ordinal));

    /// <summary>Every counter that has a value, keyed by counter id. Absent means <see cref="Unstarted"/>.</summary>
    /// <remarks>
    /// A read-only view over the live store rather than a copy, so a caller cannot cast it back to
    /// the dictionary underneath and rewrite a counter that is supposed to be server-owned.
    /// </remarks>
    internal IReadOnlyDictionary<string, int> Counters => _view;

    /// <summary>Rebuilds a counter map from storage.</summary>
    /// <param name="counters">The stored counters, keyed by counter id.</param>
    /// <returns>The rebuilt map.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="counters"/> is null.</exception>
    /// <exception cref="ArgumentException">A key is blank, or a value is negative.</exception>
    internal static PityCounters Rehydrate(IReadOnlyDictionary<string, int> counters)
    {
        ArgumentNullException.ThrowIfNull(counters);

        var store = new Dictionary<string, int>(counters.Count, StringComparer.Ordinal);

        foreach (var row in counters)
        {
            if (string.IsNullOrWhiteSpace(row.Key))
            {
                throw new ArgumentException(
                    "A stored row has a blank counter id. A counter is addressed by the id the " +
                    "tuning reader forms, and a blank one addresses every counter and none.",
                    nameof(counters));
            }

            if (row.Value < Unstarted)
            {
                throw new ArgumentException(
                    "The stored counter '" + row.Key + "' is negative. A counter counts draws since " +
                    "its guarantee last fired, and a negative one would push that guarantee further " +
                    "away the longer the player played.",
                    nameof(counters));
            }

            store[row.Key] = row.Value;
        }

        return new PityCounters(store);
    }

    /// <summary>What one counter reads. <see cref="Unstarted"/> for a counter never advanced.</summary>
    /// <param name="key">The counter id, as <c>LuckTuning</c> forms it.</param>
    /// <returns>The counter's value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    internal int Get(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return _view.TryGetValue(key, out var value) ? value : Unstarted;
    }

    /// <summary>This map with one counter set to an explicit value.</summary>
    /// <param name="key">The counter id.</param>
    /// <param name="value">The value to set. Never negative.</param>
    /// <returns>A new map; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    internal PityCounters With(string key, int value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentOutOfRangeException.ThrowIfLessThan(value, Unstarted);

        var store = new Dictionary<string, int>(_view, StringComparer.Ordinal) { [key] = value };

        return new PityCounters(store);
    }

    /// <summary>This map with one counter advanced by a single draw.</summary>
    /// <param name="key">The counter id.</param>
    /// <returns>A new map; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    internal PityCounters Advanced(string key) => With(key, checked(Get(key) + 1));

    /// <summary>This map with one counter back at <see cref="Unstarted"/>.</summary>
    /// <remarks>
    /// The only thing that resets a counter is its own guarantee being satisfied — by a forced draw
    /// or by a natural one that overshot it. Nothing else may call this.
    /// </remarks>
    /// <param name="key">The counter id.</param>
    /// <returns>A new map; this one is unchanged.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
    internal PityCounters Reset(string key) => With(key, Unstarted);
}
