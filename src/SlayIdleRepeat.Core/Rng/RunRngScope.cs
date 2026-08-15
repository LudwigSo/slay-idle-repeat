using System.Collections.ObjectModel;
using System.Globalization;

namespace SlayIdleRepeat.Core.Rng;

/// <summary>The write-back choke point for a run's draw counters: every in-run draw is opened here, and <c>GameRules.Apply</c> — never a handler — folds the final positions back into the <c>Run</c>.</summary>
/// <remarks>
/// <para>
/// The persisted stream position <em>is</em> the draw counter. A handler that drew from a stream and
/// forgot to write its counter back would break determinism silently and unreproducibly. Three
/// structural rules close that off: handlers never construct a <c>DeterministicRng</c> (this type is
/// the only construction site, enforced by an IL scan); the scope is seeded from the run's committed
/// positions and is the only thing that can produce the map <c>Run.CommitStreamPositions</c>
/// accepts (which refuses a dropped key); and <c>Apply</c> folds unconditionally on every accepted
/// command, so there is no "forget to write back" step to skip.
/// </para>
/// <para>
/// Seeded from values, never from a <c>Run</c>, because <c>Core/Rng/</c> sits below <c>Core/Model/</c>
/// in the layering and cannot name the aggregate it serves — <c>Apply</c> reads
/// <c>Run.RunSeed</c>/<c>Run.RngStreamPositions</c> and passes the two values in.
/// </para>
/// <para>
/// <c>combat</c> is not like the other streams. <c>battleSeed = Hash64(runSeed, "combat",
/// battleIndex)</c> means the <c>runSeed</c>-rooted <c>combat</c> stream is consumed exactly once per
/// battle, and its position is the next <c>battleIndex</c>, not a count of combat draws.
/// <see cref="Stream"/> refuses <c>combat</c>; <see cref="BeginBattle"/> is the only door to it, and
/// the battle stream it returns is rooted at the battle seed, restarts at 0 every battle, and is
/// never persisted — which is what makes a revived battle reproducible by construction.
/// </para>
/// <para>One scope per <c>Apply</c> call: mutable, not thread-safe, matching the single-writer-per-command model.</para>
/// <para>Meta commands get no scope at all — out-of-run draws are a different regime with a different lifetime (see <see cref="MetaDrawScope"/>).</para>
/// </remarks>
internal sealed class RunRngScope
{
    private readonly ulong _runSeed;

    /// <summary>The positions this scope was seeded from — the run's committed map, copied ordinally.</summary>
    private readonly IReadOnlyDictionary<string, ulong> _committed;

    /// <summary>One stream per name, opened lazily and reused for the rest of the command.</summary>
    private readonly Dictionary<string, DeterministicRng> _open = new(StringComparer.Ordinal);

    /// <summary>
    /// The next <c>battleIndex</c> — the <c>combat</c> stream's position, which counts battles
    /// rather than draws. Held here rather than as a <see cref="DeterministicRng"/> because the
    /// derivation is <c>SeedDerivation.BattleSeed</c>, the one sanctioned spelling.
    /// </summary>
    private ulong _nextBattleIndex;

    /// <summary>Whether <see cref="BeginBattle"/> has advanced <see cref="_nextBattleIndex"/>.</summary>
    private bool _battleStarted;

    /// <summary>Opens the scope over a run's committed seed and positions.</summary>
    /// <param name="runSeed">The run's committed seed — <c>Run.RunSeed</c>.</param>
    /// <param name="committedPositions">
    /// <c>Run.RngStreamPositions</c>, in full — seeding from anything less would break the commit,
    /// since <c>Run.CommitStreamPositions</c> refuses a map missing a key it already carries.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="committedPositions"/> is null.</exception>
    /// <exception cref="ArgumentException">A key is not a row of the stream registry.</exception>
    internal RunRngScope(ulong runSeed, IReadOnlyDictionary<string, ulong> committedPositions)
    {
        ArgumentNullException.ThrowIfNull(committedPositions);

        // Copied ordinally rather than adopted: under OrdinalIgnoreCase "DICE" IS "dice", so
        // adopting a caller's comparer could answer for a stream that doesn't exist.
        var committed = new Dictionary<string, ulong>(committedPositions.Count, StringComparer.Ordinal);

        foreach (var (streamName, position) in committedPositions)
        {
            if (!RngStreams.IsRegistered(streamName))
            {
                throw new ArgumentException(
                    "'" + (streamName ?? "null") + "' is not a row of the stream registry, so no run " +
                    "can stand at a position in it. A name that reaches this constructor and fails " +
                    "means a Run was built around a stream that cannot be drawn from.",
                    nameof(committedPositions));
            }

            committed[streamName] = position;
        }

        _runSeed = runSeed;
        _committed = committed;
        _nextBattleIndex = committed.TryGetValue(RngStreams.Combat, out var battles) ? battles : 0UL;
    }

    /// <summary>The stream <paramref name="streamName"/>, opened at the position this run committed it at and reused for the rest of the command.</summary>
    /// <param name="streamName">
    /// A row of <see cref="RngStreams"/> other than <see cref="RngStreams.Combat"/>. Always a
    /// constant off <see cref="RngStreams"/>, never a literal — a typo does not fail, it silently
    /// opens a different, valid sequence and corrupts the run unreproducibly.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="streamName"/> is null.</exception>
    /// <exception cref="ArgumentException">The name is <see cref="RngStreams.Combat"/> — use <see cref="BeginBattle"/> — or is not in the registry at all.</exception>
    internal DeterministicRng Stream(string streamName)
    {
        ArgumentNullException.ThrowIfNull(streamName);

        if (string.Equals(streamName, RngStreams.Combat, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The 'combat' stream is not opened like the others. The runSeed-rooted combat " +
                "stream is consumed EXACTLY ONCE PER BATTLE, to derive that battle's seed, so its " +
                "position is the next battleIndex and not a count of combat draws. Drawing from it " +
                "here would advance the battle counter once per die roll inside a fight. Call " +
                "BeginBattle(), then draw from the stream it returns.",
                nameof(streamName));
        }

        if (!RngStreams.IsRegistered(streamName))
        {
            throw new ArgumentException(
                "'" + streamName + "' is not a row of the 14 §8.1 stream registry, which is the " +
                "eight fixed names (" + string.Join(", ", RngStreams.FixedNames) + ") plus " +
                "minigame:{index} for a non-negative index in canonical decimal form. Draw from a " +
                "registered stream, or add a row to RngStreams.",
                nameof(streamName));
        }

        if (_open.TryGetValue(streamName, out var open))
        {
            return open;
        }

        // The one DeterministicRng construction site in SlayIdleRepeat.Core for run streams.
        var stream = new DeterministicRng(
            _runSeed,
            streamName,
            _committed.TryGetValue(streamName, out var position) ? position : 0UL);

        _open[streamName] = stream;

        return stream;
    }

    /// <summary>Starts the next battle of this run: consumes one index of the <c>runSeed</c>-rooted <c>combat</c> stream and returns that battle's seed together with a fresh, unpersisted stream rooted at it.</summary>
    /// <returns>The battle's seed — sent to the client so it can simulate the identical fight without ever holding <c>runSeed</c> — and the stream its draws come from.</returns>
    /// <remarks>The returned stream is deliberately not tracked by this scope: a revived battle restarts at draw 0 of the same battle stream, so tracking its position here would be a bug, not a feature.</remarks>
    /// <exception cref="InvalidOperationException">The run has started more battles than a <see cref="int"/> battle index can name.</exception>
    internal BattleRng BeginBattle()
    {
        if (_nextBattleIndex > int.MaxValue)
        {
            throw new InvalidOperationException(
                "This run has started " + _nextBattleIndex.ToString(CultureInfo.InvariantCulture) +
                " battles, which is more than a battle index can name: battleSeed = " +
                "Hash64(runSeed, \"combat\", battleIndex) takes a non-negative Int32, so the next " +
                "index has no seed. A run reaching this is a loop that did not terminate.");
        }

        var battleIndex = (int)_nextBattleIndex;
        var battleSeed = SeedDerivation.BattleSeed(_runSeed, battleIndex);

        _nextBattleIndex++;
        _battleStarted = true;

        // The second and last DeterministicRng construction site, rooted at the battle seed — never
        // handed to FinalPositions.
        return new BattleRng(battleIndex, battleSeed, new DeterministicRng(battleSeed, RngStreams.Combat));
    }

    /// <summary>The map <c>GameRules.Apply</c> hands to <c>Run.CommitStreamPositions</c>: every stream this run had committed, plus every stream this command actually moved.</summary>
    /// <remarks>
    /// Always a superset of the committed map — that's the contract <c>Run.CommitStreamPositions</c>
    /// enforces, since a dropped key would silently reset that stream to 0 and repeat a sequence the
    /// player already played.
    /// <para>
    /// Sparse: a stream opened but never drawn from does not appear, matching <c>Run</c>'s reading of
    /// an absent stream as draw 0 — writing eager zeros would put dead bytes in every
    /// <c>stateHash</c> for streams nothing touched.
    /// </para>
    /// </remarks>
    internal IReadOnlyDictionary<string, ulong> FinalPositions()
    {
        var positions = new Dictionary<string, ulong>(
            _committed.Count + _open.Count + 1, StringComparer.Ordinal);

        foreach (var (streamName, position) in _committed)
        {
            positions[streamName] = position;
        }

        foreach (var (streamName, stream) in _open)
        {
            // Sparse: an opened-but-undrawn, never-committed stream stays absent. A committed one is
            // already in the map at the value it still stands at.
            if (stream.Position != 0UL)
            {
                positions[streamName] = stream.Position;
            }
        }

        if (_battleStarted)
        {
            positions[RngStreams.Combat] = _nextBattleIndex;
        }

        return new ReadOnlyDictionary<string, ulong>(positions);
    }
}

/// <summary>One battle's re-rooted randomness: its index in the run, its seed, and the stream its draws come from.</summary>
/// <param name="BattleIndex">The battle's ordinal within the run — the value the <c>runSeed</c>-rooted <c>combat</c> stream stood at when the battle started.</param>
/// <param name="Seed">
/// <c>Hash64(runSeed, "combat", battleIndex)</c>, sent to the client so it can simulate the identical
/// fight; <c>runSeed</c> itself stays server-side, because a client holding it could read tomorrow's
/// draft options and drops.
/// </param>
/// <param name="Draws">The battle's own stream, starting at draw 0. Its position is never persisted — a revived battle restarts from draw 0 of the same battle stream.</param>
internal readonly record struct BattleRng(int BattleIndex, ulong Seed, DeterministicRng Draws);
