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
    /// <summary>The player as the server last projected it, or null before the first answer.</summary>
    public PlayerWireProjection? Profile { get; private set; }

    /// <summary>The run as the server last projected it, or null when no run is open.</summary>
    public RunWireProjection? Run { get; private set; }

    /// <summary>The highest sequence this mirror has taken an answer from. Zero before the first.</summary>
    public long Sequence { get; private set; }

    /// <summary>The hash the last applied answer carried, or null before one carried a hash.</summary>
    public string? StateHash { get; private set; }

    /// <summary>Whether the most recent apply moved anything.</summary>
    /// <remarks>
    /// The flag a scene polls, so it can redraw on a change without holding a subscription. It is the
    /// last answer, not a latch: a later apply that changes nothing clears it.
    /// </remarks>
    public bool LastChanged { get; private set; }

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
            return LastChanged = false;
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
        Sequence = Math.Max(Sequence, sequence);

        // A null hash is an exchange that read no state — a refusal the server answered without
        // touching the row. There is nothing to compare it against and nothing it could have moved,
        // so it is not the same case as a hash that matches and it is not a change either.
        if (stateHash is null || string.Equals(StateHash, stateHash, StringComparison.Ordinal))
        {
            return LastChanged = false;
        }

        StateHash = stateHash;

        // Kept rather than cleared when an answer carries none: this class records what the server
        // said and nothing else, and "the response omitted the run" is not the server saying the run
        // is gone.
        Profile = profile ?? Profile;
        Run = run ?? Run;

        return LastChanged = true;
    }
}
