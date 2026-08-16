using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.UseCases;

/// <summary>A player asking for their own state, optionally for one particular run of theirs.</summary>
/// <param name="Player">Whose state to read.</param>
/// <param name="Run">
/// A run of theirs to read, or <c>null</c> for whatever run they are in now. A run they have already
/// finished is served from the archive.
/// </param>
public sealed record ReadOwnStateRequest(PlayerId Player, RunId? Run);

/// <summary>What a read found.</summary>
public enum OwnStateLookup
{
    /// <summary>The state asked for exists and is in the view.</summary>
    Found = 1,

    /// <summary>Nothing is stored for that player at all.</summary>
    NoSuchPlayer = 2,

    /// <summary>
    /// That player has no such run. Also the answer when the run exists but belongs to someone else —
    /// the two are deliberately indistinguishable, so a caller cannot probe for another player's runs.
    /// </summary>
    NoSuchRun = 3,
}

/// <summary>The state a player reads about themselves: the persisted rows, not a parallel shape.</summary>
/// <param name="Player">The player's row.</param>
/// <param name="Run">The run asked for, or <c>null</c> when the player is in none.</param>
/// <remarks>
/// The persisted rows themselves rather than a read-model copy of them: a second shape for the same
/// state is a second thing to keep in step, and this read has no field the write side does not.
/// </remarks>
public sealed record OwnStateView(PlayerSnapshot Player, RunSnapshot? Run)
{
    /// <summary>How stale this view may be: not at all.</summary>
    /// <remarks>
    /// A player must see their own accepted command in the very next read — the client has already
    /// animated it. Every other read model in the game may declare a budget; this one cannot.
    /// </remarks>
    public static TimeSpan StalenessBudget => TimeSpan.Zero;
}

/// <summary>What a read answered: the lookup, and the view when there is one.</summary>
/// <param name="Lookup">What was found.</param>
/// <param name="View">The state, or <c>null</c> when nothing was found.</param>
public sealed record OwnStateResult(OwnStateLookup Lookup, OwnStateView? View);

/// <summary>The read side: persisted rows out, nothing in.</summary>
/// <remarks>
/// It never rehydrates an aggregate, which is what makes "a read runs no rule" structural rather than
/// a discipline — there is no aggregate here for a rule to be invoked on. It writes nothing either,
/// so reading cannot slide a run's expiry or stamp a player as active.
/// </remarks>
public sealed class ReadOwnStateUseCase
{
    private readonly WorldSliceStore _store;

    /// <summary>Builds the use case over the store it reads through.</summary>
    /// <param name="store">Where the rows live.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> is null.</exception>
    public ReadOwnStateUseCase(WorldSliceStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    /// <summary>Reads a player's own state.</summary>
    /// <param name="request">Whose state, and which run of theirs.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The lookup and, when it found something, the view.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is null.</exception>
    public async Task<OwnStateResult> ReadAsync(ReadOwnStateRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stored = await _store.ReadSnapshotsAsync(request.Player, ct);

        if (stored is null)
        {
            return new OwnStateResult(OwnStateLookup.NoSuchPlayer, null);
        }

        if (request.Run is not { } asked)
        {
            return Found(stored.Player, stored.Run);
        }

        if (stored.Run is { } current && current.Id == asked)
        {
            return Found(stored.Player, current);
        }

        var archived = await _store.ReadArchivedRunAsync(asked, ct);

        // The archive key carries no owner, so ownership is checked here or not at all — and a
        // stranger's run answers exactly as an absent one does, or the answer confirms the id exists.
        return archived is not null && archived.PlayerId == request.Player
            ? Found(stored.Player, archived)
            : new OwnStateResult(OwnStateLookup.NoSuchRun, null);
    }

    private static OwnStateResult Found(PlayerSnapshot player, RunSnapshot? run) =>
        new(OwnStateLookup.Found, new OwnStateView(player, run));
}
