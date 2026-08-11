namespace SlayIdleRepeat.Core.Rng;

/// <summary>
/// 🔒 `14` §8.0 / §8.1 — a counter-based draw stream. Draw <c>i</c> of stream <c>s</c> over seed
/// <c>r</c> is <c>Hash64(r, s, i)</c>. Stateless but for the counter.
/// </summary>
/// <remarks>
/// <para>
/// There is no generator here. Nothing evolves, so there is nothing to snapshot or restore:
/// <see cref="Position"/> is the entire persistable state, and rehydrating a stream is
/// <c>new DeterministicRng(runSeed, name, position)</c> and nothing else. The
/// <c>"rngStreamStates": { "dice": 12, "board": 8 }</c> on the wire (`14` §2.3) literally means
/// <i>twelve draws consumed from <c>dice</c>, eight from <c>board</c></i>.
/// </para>
/// <para>
/// 🔒 <b>Every call consumes exactly one draw index</b> — <see cref="WeightedPick"/> included.
/// That is what makes the persisted counter meaningful and auditable: <see cref="Position"/>
/// equals the number of calls ever made on the stream. A rejected call is not a call: every
/// accessor validates its arguments before it draws.
/// </para>
/// <para>
/// Draws are randomly accessible, which is the point. A revive replay, a resync or a bug-report
/// reproduction re-derives any draw without replaying the ones before it.
/// </para>
/// </remarks>
public sealed class DeterministicRng
{
    /// <summary>2^-53 — the scale of the standard 53-bit double construction.</summary>
    private const double FiftyThreeBitUnit = 1.0 / 9007199254740992.0;

    private readonly ulong _seed;
    private readonly string _streamName;

    /// <summary>The next draw index. The only mutable state this type has, by design.</summary>
    private ulong _position;

    /// <summary>Opens a stream over a seed, optionally rehydrated at a persisted position.</summary>
    /// <param name="seed">
    /// The run seed (`02` §2), a <c>battleSeed</c> (`14` §8.1), or a server-issued
    /// <c>CommandSeed</c> for an out-of-run meta command (`30` §3).
    /// </param>
    /// <param name="streamName">A row of <see cref="RngStreams"/>. Anything else is rejected.</param>
    /// <param name="position">The next draw index — the persisted counter. Zero for a fresh stream.</param>
    /// <exception cref="ArgumentNullException">The stream name is null.</exception>
    /// <exception cref="ArgumentException">The stream name is not in the registry of `14` §8.1.</exception>
    public DeterministicRng(ulong seed, string streamName, ulong position = 0)
    {
        ArgumentNullException.ThrowIfNull(streamName);

        if (!RngStreams.IsRegistered(streamName))
        {
            throw new ArgumentException(
                $"'{streamName}' is not a stream in the registry of 14 §8.1. Draw from one of " +
                $"{string.Join(", ", RngStreams.FixedNames)} or {RngStreams.MinigamePrefix}{{index}}, " +
                "or add a row to the registry — a name that is merely spelled differently is a " +
                "different, silently valid sequence.",
                nameof(streamName));
        }

        _seed = seed;
        _streamName = streamName;
        _position = position;
    }

    /// <summary>
    /// The <b>next</b> draw index, and the entire persistable state of this stream. Equal to the
    /// number of calls ever made on it when the stream started at zero.
    /// </summary>
    public ulong Position => _position;

    /// <summary>The top 32 bits of the draw.</summary>
    public uint NextUInt() => (uint)(NextDraw() >> 32);

    /// <summary>
    /// A value in [0,1): <c>(draw &gt;&gt; 11) * 2^-53</c>, the standard 53-bit construction.
    /// </summary>
    /// <remarks>
    /// ⚠️ This is a <b>draw</b>, not an accumulation point. `14` §8.2's
    /// <c>Math.Round(x, 4)</c> rule governs accumulated combat values; rounding here would throw
    /// away 49 of the 53 bits of every draw the game makes. The construction is exact in binary
    /// floating point — a 53-bit integer times a power of two — so it is already identical on
    /// every target platform, which is what §8.2's cross-platform job is checking for.
    /// </remarks>
    public double NextDouble() => UnitInterval(NextDraw());

    /// <summary>
    /// A value in <c>[minInclusive, maxExclusive)</c>:
    /// <c>min + (int)(draw % (ulong)(max - min))</c>.
    /// </summary>
    /// <remarks>
    /// 🔒 The modulo bias — below <c>range / 2^64</c> — is <b>accepted</b> by `14` §8.0. Rejection
    /// sampling would consume a variable number of draw indices per call and destroy the
    /// one-call-one-index property the whole persistence model rests on. Do not "improve" this:
    /// a different algorithm here is a determinism break.
    /// <para>
    /// The width is computed in 64 bits because the specification's literal expression overflows
    /// <see cref="int"/> at the extremes (<c>int.MaxValue - int.MinValue</c> is -1 in 32-bit
    /// arithmetic, which would turn the modulus into <see cref="ulong.MaxValue"/>). The draw and
    /// the modulo are unchanged; only the intermediate arithmetic is wide enough to be right.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The range is empty or inverted.</exception>
    public int Range(int minInclusive, int maxExclusive)
    {
        if (maxExclusive <= minInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive),
                maxExclusive,
                $"The range [{minInclusive}, {maxExclusive}) is empty — there is no value to draw.");
        }

        var width = (ulong)((long)maxExclusive - minInclusive);

        return (int)(minInclusive + (long)(NextDraw() % width));
    }

    /// <summary>
    /// 🔒 One draw: <c>x = unit interval × Σ weights</c>; walk the table <b>in order</b>; the
    /// first item whose cumulative weight <b>exceeds</b> <c>x</c>.
    /// </summary>
    /// <remarks>
    /// One draw, not two. The strict comparison is what makes a zero-weight row unreachable
    /// wherever it sits in the table — content disables a row by zeroing its weight, and a
    /// disabled row that could still be picked is a bug that surfaces once in ten thousand runs.
    /// </remarks>
    /// <exception cref="ArgumentNullException">The table is null.</exception>
    /// <exception cref="ArgumentException">
    /// The table is empty, carries a negative or non-finite weight, or has no positive weight at
    /// all. Each is a content error rather than a draw, and none of them consumes a draw index.
    /// </exception>
    public T WeightedPick<T>(IReadOnlyList<(T item, double weight)> table)
    {
        // Validated before the draw: a rejected call must consume no draw index.
        var total = TotalWeight(table);

        return Walk(UnitInterval(NextDraw()) * total, table);
    }

    /// <summary>
    /// The weighted walk on its own, at an explicit unit interval rather than at a draw.
    /// </summary>
    /// <remarks>
    /// Exposed to the domain test suite (`30` §11.3) so the walk's boundaries can be asserted at
    /// exact values — including the top of the unit interval, 1 − 2^-53, which no seed can be
    /// searched for.
    /// </remarks>
    internal static T PickAt<T>(double unitInterval, IReadOnlyList<(T item, double weight)> table)
    {
        var total = TotalWeight(table);

        return Walk(unitInterval * total, table);
    }

    private static T Walk<T>(double threshold, IReadOnlyList<(T item, double weight)> table)
    {
        var cumulative = 0.0;
        var lastWeighted = -1;

        for (var i = 0; i < table.Count; i++)
        {
            var weight = table[i].weight;
            if (weight <= 0.0)
            {
                continue;
            }

            cumulative += weight;
            lastWeighted = i;

            if (cumulative > threshold)
            {
                return table[i].item;
            }
        }

        // Unreachable while the walk accumulates in the same order TotalWeight summed in — the
        // final cumulative weight is then bit-identical to the total, and the unit interval is
        // strictly below 1. It is written out anyway because that invariant lives in two methods
        // rather than one: should a future edit sum the table differently, this turns a run-time
        // exception in the middle of a battle into the last weighted row, which is the answer the
        // specification's own wording gives at the top of the range.
        if (lastWeighted < 0)
        {
            // TotalWeight has already proved a positive weight exists, so the only way to get
            // here is a table that answered differently on the two passes. Named explicitly
            // because table[-1] would surface it as an index exception from whichever list the
            // caller happened to pass, which says nothing about what actually went wrong.
            throw new InvalidOperationException(
                "The weighted table reported no positive weight on the walk after reporting one " +
                "on the sum. A table must not change while a pick is being taken.");
        }

        return table[lastWeighted].item;
    }

    /// <summary>
    /// Σ weights, validating the table as it goes. Summed left to right, in the same order
    /// <see cref="Walk"/> accumulates, so the final cumulative weight equals this exactly.
    /// </summary>
    private static double TotalWeight<T>(IReadOnlyList<(T item, double weight)> table)
    {
        ArgumentNullException.ThrowIfNull(table);

        if (table.Count == 0)
        {
            throw new ArgumentException("A weighted table needs at least one row.", nameof(table));
        }

        var total = 0.0;
        for (var i = 0; i < table.Count; i++)
        {
            var weight = table[i].weight;
            if (!double.IsFinite(weight) || weight < 0.0)
            {
                throw new ArgumentException(
                    $"Row {i} has weight {weight}. A weight must be a finite, non-negative number — " +
                    "anything else makes the cumulative walk non-monotonic and its answer arbitrary.",
                    nameof(table));
            }

            total += weight;
        }

        if (total <= 0.0)
        {
            throw new ArgumentException(
                "Every row of the table has weight zero, so no row can be picked.",
                nameof(table));
        }

        return total;
    }

    private static double UnitInterval(ulong draw) => (draw >> 11) * FiftyThreeBitUnit;

    /// <summary>
    /// The one place the counter advances: <c>Hash64(seed, streamName, position)</c>, then
    /// <c>position + 1</c>.
    /// </summary>
    private ulong NextDraw()
    {
        if (_position == ulong.MaxValue)
        {
            throw new InvalidOperationException(
                $"Stream '{_streamName}' has reached the reserved final draw index. The counter " +
                "is the entire persistable state of a stream, so it must never wrap — the last " +
                "index is given up rather than kept a flag to remember it by.");
        }

        var draw = Hash64.Of(_seed, _streamName, _position);
        _position++;

        return draw;
    }
}
