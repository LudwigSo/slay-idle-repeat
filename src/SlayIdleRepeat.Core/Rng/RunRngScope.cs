using System.Collections.ObjectModel;
using System.Globalization;

namespace SlayIdleRepeat.Core.Rng;

/// <summary>
/// 🔒 The write-back choke point for a run's `14` §8.1 draw counters (M1 kickoff decision 5): every
/// in-run draw is opened here, and <c>GameRules.Apply</c> — never a handler — folds the final
/// positions back into the <c>Run</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The hazard this closes.</b> `14` §8.1 makes <em>the persisted stream position the draw
/// counter</em>. A handler that drew from a stream and forgot to write its counter back would break
/// determinism <b>silently and unreproducibly</b>: the run replays with different draws, the
/// client/server parity test (`14` §13) compares two different universes, and nothing goes red. The
/// three rules that close it are structural rather than remembered —
/// </para>
/// <list type="number">
///   <item><b>Handlers never construct a <c>DeterministicRng</c>.</b> Enforced by
///   <c>DomainPurityTests.DeterministicRng_is_constructed_only_inside_Core_Rng</c>, an IL scan over
///   <c>SlayIdleRepeat.Core</c>: this type is the <b>only</b> construction site in the assembly.</item>
///   <item><b>The scope is seeded from the run's <em>committed</em> positions</b> and is the only
///   thing that can produce the map <c>Run.CommitStreamPositions</c> accepts — that seam takes the
///   <b>whole</b> map and refuses a dropped key (a superset contract, not a merge), so a handler
///   that wanted to hand-write one position would have to reconstruct the entire committed set.</item>
///   <item><b><c>Apply</c> folds, unconditionally.</b> Forgetting is not an available failure: the
///   handler has no <c>Run</c>-writing call to forget, and the fold runs on every accepted command.
///   A run whose positions changed <em>underneath</em> the handler is caught and raised as a defect.</item>
/// </list>
/// <para>
/// 🔒 <b>Seeded from values, never from a <c>Run</c>.</b> <c>Core/Rng/</c> sits below
/// <c>Core/Model/</c> in `30` §11.4's layering and
/// <c>AccessibilityBoundaryTests.Core_internal_layering_holds</c> enforces it, so this type cannot
/// name the aggregate it exists to serve. <c>Apply</c> reads <c>Run.RunSeed</c> and
/// <c>Run.RngStreamPositions</c> and passes the two values in.
/// </para>
/// <para>
/// 🔒 <b><c>combat</c> is not like the others, and this type refuses to pretend it is.</b> `14`
/// §8.1 derives <c>battleSeed = Hash64(runSeed, "combat", battleIndex)</c> and then draws combat
/// value <c>i</c> as <c>Hash64(battleSeed, "combat", i)</c>. So the <c>runSeed</c>-rooted
/// <c>combat</c> stream is consumed <b>exactly once per battle</b> and its position is <b>the next
/// <c>battleIndex</c></b> — not a count of combat draws. <see cref="Stream"/> therefore <b>refuses
/// <c>combat</c></b> and <see cref="BeginBattle"/> is the only door to it; the returned battle
/// stream is rooted at the battle seed, restarts at 0 for every battle and is <b>never
/// persisted</b>, which is exactly what makes §8.1's <em>"a revived battle restarts from draw 0 of
/// the same battle stream: reproducible by construction"</em> true.
/// </para>
/// <para>
/// <b>Counting.</b> The scope does not wrap the draw API — <c>DeterministicRng.Position</c> already
/// <em>is</em> the count, and re-declaring <c>NextUInt</c>/<c>NextDouble</c>/<c>Range</c>/
/// <c>WeightedPick</c> here would be a second copy of the one-call-one-index contract to keep in
/// step. What the scope owns is <b>which</b> streams exist, <b>where</b> each starts, and that each
/// name yields the <em>same</em> stream for the whole command: a second
/// <c>Stream(RngStreams.Dice)</c> inside one <c>Apply</c> must continue the sequence, not restart
/// it at the position the command began with.
/// </para>
/// <para>
/// ⚠️ <b>One scope per <c>Apply</c> call.</b> It is mutable and it is not thread-safe; `30` §4 makes
/// a player a single writer, one command at a time.
/// </para>
/// <para>
/// ⚠️ <b>Meta commands get no scope at all.</b> Out-of-run draws are
/// <c>Hash64(GameContext.CommandSeed, stream, i)</c> from <c>i = 0</c> with <b>no persisted
/// counter</b> (`14` §8.1, `30` §3), which is a different regime with a different lifetime — mixing
/// the two here would give a meta draw a counter the design says it must not have.
/// </para>
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

    /// <summary>
    /// Opens the scope over a run's committed seed and positions.
    /// </summary>
    /// <param name="runSeed">`02` §2's committed <c>runSeed</c> — <c>Run.RunSeed</c>.</param>
    /// <param name="committedPositions">
    /// 🔒 <c>Run.RngStreamPositions</c>, in full. Seeding from anything less is not a shortcut but a
    /// broken commit: <c>Run.CommitStreamPositions</c> refuses a map missing a key it already
    /// carries, so a scope that dropped one could never fold back at all.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="committedPositions"/> is null.</exception>
    /// <exception cref="ArgumentException">A key is not a row of the `14` §8.1 registry.</exception>
    internal RunRngScope(ulong runSeed, IReadOnlyDictionary<string, ulong> committedPositions)
    {
        ArgumentNullException.ThrowIfNull(committedPositions);

        // Copied ORDINALLY rather than adopted, for the reason Run.CommitStreamPositions copies:
        // under OrdinalIgnoreCase the key "DICE" IS "dice", so a scope that adopted a caller's
        // comparer would answer for a stream 14 §8.1 does not have.
        var committed = new Dictionary<string, ulong>(committedPositions.Count, StringComparer.Ordinal);

        foreach (var (streamName, position) in committedPositions)
        {
            if (!RngStreams.IsRegistered(streamName))
            {
                throw new ArgumentException(
                    "'" + (streamName ?? "null") + "' is not a row of the 14 §8.1 stream registry, so " +
                    "no run can stand at a position in it. The same RngStreams.IsRegistered predicate " +
                    "guards DeterministicRng's constructor and Run.CommitStreamPositions; a name that " +
                    "reaches this constructor and fails it means a Run was built around a stream that " +
                    "cannot be drawn from.",
                    nameof(committedPositions));
            }

            committed[streamName] = position;
        }

        _runSeed = runSeed;
        _committed = committed;
        _nextBattleIndex = committed.TryGetValue(RngStreams.Combat, out var battles) ? battles : 0UL;
    }

    /// <summary>
    /// The stream <paramref name="streamName"/>, opened at the position this run committed it at and
    /// reused for the rest of the command.
    /// </summary>
    /// <param name="streamName">
    /// A row of <see cref="RngStreams"/> other than <see cref="RngStreams.Combat"/>. 🔒 Always a
    /// constant off <see cref="RngStreams"/>, never a literal — a typo does not fail, it silently
    /// opens a different, perfectly valid sequence, and the run it corrupts is by definition
    /// unreproducible.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="streamName"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The name is <see cref="RngStreams.Combat"/> — use <see cref="BeginBattle"/> — or is not in
    /// the registry at all.
    /// </exception>
    internal DeterministicRng Stream(string streamName)
    {
        ArgumentNullException.ThrowIfNull(streamName);

        if (string.Equals(streamName, RngStreams.Combat, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The 'combat' stream is not opened like the others. 14 §8.1 re-roots it: the " +
                "runSeed-rooted combat stream is consumed EXACTLY ONCE PER BATTLE, to derive that " +
                "battle's seed, so its position is the next battleIndex and not a count of combat " +
                "draws. Drawing from it here would advance the battle counter once per die roll " +
                "inside a fight, and every later battle in the run would be seeded from a number no " +
                "replay could reproduce. Call BeginBattle(), then draw from the stream it returns — " +
                "those draws restart at 0 for every battle and are never persisted, which is what " +
                "makes a revived battle reproducible by construction.",
                nameof(streamName));
        }

        if (!RngStreams.IsRegistered(streamName))
        {
            throw new ArgumentException(
                "'" + streamName + "' is not a row of the 14 §8.1 stream registry, which is the eight " +
                "fixed names (" + string.Join(", ", RngStreams.FixedNames) + ") plus minigame:{index} " +
                "for a non-negative index in canonical decimal form. Draw from a registered stream, " +
                "or add a row to RngStreams — a name that is merely spelled differently is a " +
                "different, silently valid sequence.",
                nameof(streamName));
        }

        if (_open.TryGetValue(streamName, out var open))
        {
            return open;
        }

        // 🔒 The ONE DeterministicRng construction site in SlayIdleRepeat.Core, and the architecture
        // rule named in this type's remarks is what keeps it the only one.
        var stream = new DeterministicRng(
            _runSeed,
            streamName,
            _committed.TryGetValue(streamName, out var position) ? position : 0UL);

        _open[streamName] = stream;

        return stream;
    }

    /// <summary>
    /// 🔒 `14` §8.1 — starts the next battle of this run: consumes one index of the
    /// <c>runSeed</c>-rooted <c>combat</c> stream and returns that battle's seed together with a
    /// fresh, <b>unpersisted</b> stream rooted at it.
    /// </summary>
    /// <returns>
    /// The battle's seed — which `14` §2.4 sends to the client so it can simulate the identical
    /// fight <b>without ever holding <c>runSeed</c></b> — and the stream its draws come from.
    /// </returns>
    /// <remarks>
    /// The returned stream is deliberately <b>not</b> tracked by this scope: its position is never
    /// folded into the <c>Run</c>, because §8.1 restarts a revived battle at draw 0 of the same
    /// battle stream. Tracking it would be the bug, not the feature.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The run has started more battles than a <see cref="int"/> battle index can name.
    /// </exception>
    internal BattleRng BeginBattle()
    {
        if (_nextBattleIndex > int.MaxValue)
        {
            throw new InvalidOperationException(
                "This run has started " + _nextBattleIndex.ToString(CultureInfo.InvariantCulture) +
                " battles, which is more than a battle index can name: 14 §8.1's " +
                "battleSeed = Hash64(runSeed, \"combat\", battleIndex) takes a non-negative Int32, so " +
                "the next index has no seed. A run reaching this is a loop that did not terminate, " +
                "not a long session.");
        }

        var battleIndex = (int)_nextBattleIndex;
        var battleSeed = SeedDerivation.BattleSeed(_runSeed, battleIndex);

        _nextBattleIndex++;
        _battleStarted = true;

        // 🔒 The second and last DeterministicRng construction site. Rooted at the BATTLE seed, so
        // its position counts draws within this battle — and it is never handed to FinalPositions.
        return new BattleRng(battleIndex, battleSeed, new DeterministicRng(battleSeed, RngStreams.Combat));
    }

    /// <summary>
    /// 🔒 The map <c>GameRules.Apply</c> hands to <c>Run.CommitStreamPositions</c>: every stream this
    /// run had committed, plus every stream this command actually moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// 🔒 <b>A superset of the committed map, always.</b> That is not politeness towards
    /// <c>Run.CommitStreamPositions</c> — it is the contract that seam enforces, because a dropped
    /// key would silently reset that stream to 0 and the next draw from it would repeat a sequence
    /// the player has already played.
    /// </para>
    /// <para>
    /// ⚠️ <b>Sparse: a stream opened but never drawn from does not appear.</b> `14` §2.3's wire echo
    /// <c>{"dice":12,"board":8}</c> shows only the streams that moved, and <c>Run</c>'s map reads an
    /// absent stream as draw 0 — so writing eager zeros would put dead bytes in every
    /// <c>stateHash</c> for streams nothing has touched.
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
            // Sparse: an opened-but-undrawn stream that was never committed stays absent. One that
            // WAS committed is already in the map at its committed value, which is what the stream
            // still stands at.
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

/// <summary>
/// 🔒 `14` §8.1 — one battle's re-rooted randomness: its index in the run, its seed, and the stream
/// its draws come from.
/// </summary>
/// <param name="BattleIndex">
/// The battle's ordinal within the run — the value the <c>runSeed</c>-rooted <c>combat</c> stream
/// stood at when the battle started, and the one persisted number this whole type produces.
/// </param>
/// <param name="Seed">
/// <c>Hash64(runSeed, "combat", battleIndex)</c>. `14` §2.4 sends it to the client so it can
/// simulate the identical fight; `02` §2 keeps <c>runSeed</c> itself on the server, because a client
/// holding it could read tomorrow's draft options and drops.
/// </param>
/// <param name="Draws">
/// 🔒 The battle's own stream, starting at draw 0. Its position is <b>never persisted</b>: `14`
/// §8.1 restarts a revived battle from draw 0 of the same battle stream, which needs no support
/// beyond constructing the stream again.
/// </param>
internal readonly record struct BattleRng(int BattleIndex, ulong Seed, DeterministicRng Draws);
