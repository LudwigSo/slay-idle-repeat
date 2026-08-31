using SlayIdleRepeat.Application.Wire;

namespace SlayIdleRepeat.Client.Game.Net;

/// <summary>
/// 🔒 A denormalised local copy of what the server last said, kept so a screen has something to draw
/// while it is offline — and <b>never authoritative</b>.
/// </summary>
/// <remarks>
/// <para>
/// That is a structural claim, not a promise. Nothing here computes: there is no rule, no derived
/// number, no merge of a local guess with a server answer. Every member is a value that arrived in a
/// response and is handed back unchanged, so the worst a stale mirror can do is show an old screen —
/// it can never produce a state the server never held.
/// </para>
/// <para>
/// 🔒 <b>Change is decided by <c>stateHash</c>, not by comparing projections.</b> The hash covers the
/// whole row on the server's side and is what the wire carries it for; diffing two projections by
/// hand would be a second, weaker definition of "the same state" that drifts on the first field
/// added to either record.
/// </para>
/// <para>
/// ⚠️ <b>No event.</b> Both applies answer with a bool and <see cref="LastChanged"/> records the most
/// recent answer, because a scene subscribing to a C# event needs a matching disconnect on the way
/// out and a missing one is a defect class this project reviews for. A caller that needs to know
/// whether anything moved reads the return value at the moment it applies.
/// </para>
/// </remarks>
public sealed class StateMirror
{
    /// <summary>Everything the mirror holds, as one value that is replaced rather than edited.</summary>
    /// <remarks>
    /// 🔒 <b>One immutable value behind a volatile reference, not five mutable fields.</b> The applies
    /// run on a thread-pool thread — the ladder awaits the port with <c>ConfigureAwait(false)</c> —
    /// while the connection overlay polls these members on the engine's frame thread every frame.
    /// Five ordinary field stores give a reader no ordering at all, so the frame thread could see a
    /// new <c>Run</c> beside the previous <c>StateHash</c>: a screen drawn from half of one answer
    /// and half of another, with the hash that decides whether to announce a resync being the stale
    /// half. Publishing the whole reading at once makes a torn read unrepresentable.
    /// </remarks>
    private sealed record Reading(
        PlayerWireProjection? Profile,
        RunWireProjection? Run,
        long Sequence,
        string? StateHash,
        bool LastChanged);

    private volatile Reading _held = new(null, null, 0, null, false);

    /// <summary>The player as the server last projected it, or null before the first answer.</summary>
    public PlayerWireProjection? Profile => _held.Profile;

    /// <summary>The run as the server last projected it, or null when no run is open.</summary>
    public RunWireProjection? Run => _held.Run;

    /// <summary>The highest sequence this mirror has taken an answer from. Zero before the first.</summary>
    public long Sequence => _held.Sequence;

    /// <summary>The hash the last applied answer carried, or null before one carried a hash.</summary>
    public string? StateHash => _held.StateHash;

    /// <summary>Whether the most recent apply moved anything.</summary>
    /// <remarks>
    /// The flag a scene polls, so it can redraw on a change without holding a subscription. It is the
    /// last answer, not a latch: a later apply that changes nothing clears it.
    /// </remarks>
    public bool LastChanged => _held.LastChanged;

    /// <summary>Takes one command outcome, if it is not older than what is already held.</summary>
    /// <param name="result">The outcome, as the port answered it.</param>
    /// <returns>True when the mirror moved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="result"/> is null.</exception>
    /// <remarks>
    /// ⚠️ An answer whose sequence is BEHIND what is held is dropped rather than applied. Commands
    /// are drained in order but their answers are separate exchanges, and a slow answer to an old
    /// command arriving after a fast answer to a new one would otherwise walk the screen backwards
    /// to a state the player has already left.
    /// </remarks>
    public bool Apply(WireCommandResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Sequence < Sequence)
        {
            _held = _held with { LastChanged = false };

            return false;
        }

        return Record(result.Sequence, result.StateHash, result.Profile, result.Run);
    }

    /// <summary>Takes a whole run-state read.</summary>
    /// <param name="state">The read, as the port answered it.</param>
    /// <returns>True when the mirror moved — which is what decides whether a resync is worth announcing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> is null.</exception>
    /// <remarks>
    /// A read carries no sequence of its own when the ledger no longer knows the scope, so the
    /// mirror keeps the sequence it already had rather than resetting it to zero: forgetting how far
    /// it had got would make the next command's answer look like an out-of-order one.
    /// </remarks>
    public bool Apply(WireRunState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return Record(state.Sequence ?? Sequence, state.StateHash, state.Profile, state.Run);
    }

    private bool Record(long sequence, string? stateHash, PlayerWireProjection? profile, RunWireProjection? run)
    {
        var held = _held;
        var advanced = Math.Max(held.Sequence, sequence);

        // A null hash is an exchange that read no state — a refusal the server answered without
        // touching the row. There is nothing to compare it against and nothing it could have moved,
        // so it is not the same case as a hash that matches and it is not a change either.
        if (stateHash is null || string.Equals(held.StateHash, stateHash, StringComparison.Ordinal))
        {
            _held = held with { Sequence = advanced, LastChanged = false };

            return false;
        }

        // Kept rather than cleared when an answer carries none: this class records what the server
        // said and nothing else, and "the response omitted the run" is not the server saying the run
        // is gone.
        _held = new Reading(
            profile ?? held.Profile,
            run ?? held.Run,
            advanced,
            stateHash,
            LastChanged: true);

        return true;
    }
}
