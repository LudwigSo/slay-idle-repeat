namespace SlayIdleRepeat.Core.Rng;

/// <summary>The out-of-run draw regime: draw <c>i</c> of stream <c>s</c> is <c>Hash64(GameContext.CommandSeed, s, i)</c>, with <c>i</c> starting at 0 for each command and no persisted counter.</summary>
/// <remarks>
/// A different regime from <see cref="RunRngScope"/>, not a variant of it — the difference is
/// lifetime. A run stream's position is authoritative run state that <c>Apply</c> folds back into
/// <c>Run</c>. A meta draw has none of that: the command is atomic and idempotency replays its
/// stored outcome, so a meta draw can never be re-rolled by resubmission and needs no counter.
/// <para>
/// This type names no aggregate — <c>Core/Rng/</c> sits below <c>Core/Model/</c> and cannot, so
/// nothing here can accidentally move a run's stream positions.
/// </para>
/// <para>
/// One scope per <c>Apply</c> call, mutable and not thread-safe. What it guarantees is that one
/// stream name yields the same stream for the whole command: a handler that asks for a stream twice
/// continues the sequence rather than restarting at 0 and drawing the same value twice.
/// </para>
/// <para>
/// There is deliberately no second entry point taking an explicit draw index. Meta draws are already
/// randomly addressable as a pure function of seed and index; a second API over the same seed would
/// let one command both count on a stream and address it, double-spending an index with both calls
/// individually "correct".
/// </para>
/// </remarks>
internal sealed class MetaDrawScope
{
    private readonly ulong _commandSeed;

    /// <summary>One stream per name, opened lazily and reused for the rest of the command.</summary>
    /// <remarks>Ordinal: under <c>OrdinalIgnoreCase</c> the key <c>"DROPS"</c> is <c>"drops"</c>, which would answer for a stream that doesn't exist.</remarks>
    private readonly Dictionary<string, DeterministicRng> _open = new(StringComparer.Ordinal);

    /// <summary>Opens the scope over one command's server-issued seed.</summary>
    /// <param name="commandSeed">
    /// <c>GameContext.CommandSeed</c>, already known to be present. <c>0</c> is a legitimate seed,
    /// not an absence — the "is there a seed" question is answered once by <c>HandlerInput.MetaDraws</c>.
    /// </param>
    internal MetaDrawScope(ulong commandSeed) => _commandSeed = commandSeed;

    /// <summary>The stream this command draws <paramref name="streamName"/> from, starting at draw 0 and reused for the rest of the command.</summary>
    /// <param name="streamName">A row of the stream registry — see <see cref="RngStreams"/>.</param>
    /// <returns>
    /// The stream. Its <c>Position</c> is a within-command count and is never persisted: nothing
    /// folds it back, and the next command over the same seed starts at 0 again.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="streamName"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="streamName"/> is not a row of the registry. Raised by <see cref="DeterministicRng"/>'s own guard.</exception>
    internal DeterministicRng Stream(string streamName)
    {
        ArgumentNullException.ThrowIfNull(streamName);

        if (_open.TryGetValue(streamName, out var open))
        {
            return open;
        }

        // Position 0, explicitly and always — there is no committed map to seed from here, unlike
        // RunRngScope, and writing the default out makes that choice visible rather than implicit.
        var stream = new DeterministicRng(_commandSeed, streamName, position: 0);

        _open.Add(streamName, stream);

        return stream;
    }
}
