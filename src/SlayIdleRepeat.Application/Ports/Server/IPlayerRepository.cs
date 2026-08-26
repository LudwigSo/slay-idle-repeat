using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Ports.Server;

/// <summary>The authoritative store of player profiles — the system-of-record row behind every command.</summary>
/// <remarks>
/// <para>
/// <c>23</c> §4.2's persistence port, adapted: the section's <c>CreateAnonymousAsync</c> takes a
/// <c>DeviceFingerprint</c> this repository deliberately does not model — nothing normative defines
/// one, and the device credential (<c>deviceId</c> + hashed secret) arrives with M5-06's auth. So
/// creation takes the initial profile the server minted and the device linkage stays with the task
/// that owns the vocabulary.
/// </para>
/// <para>
/// <see cref="SaveAsync"/> is an upsert of an EXISTING player's committed state — the write every
/// accepted command makes. <see cref="CreateAnonymousAsync"/> is the one door a new identity enters
/// through, and it refuses an id that already has a row rather than overwriting a real account with
/// a fresh one.
/// </para>
/// <para>
/// A profile whose <c>ActiveRun</c> is present commits the player row and the run's row as one
/// atomic effect: a reader of either side sees both moves or neither.
/// </para>
/// </remarks>
public interface IPlayerRepository
{
    /// <summary>The player's stored profile, or <c>null</c> when no row exists for this id.</summary>
    /// <param name="id">The player.</param>
    /// <param name="ct">Cancellation.</param>
    Task<PlayerProfile?> GetAsync(PlayerId id, CancellationToken ct);

    /// <summary>Commits a player's profile — the player row and, when present, its run's row, atomically.</summary>
    /// <param name="profile">The state to commit, exactly as the domain produced it.</param>
    /// <param name="ct">Cancellation.</param>
    Task SaveAsync(PlayerProfile profile, CancellationToken ct);

    /// <summary>Creates the row for a newly issued anonymous account and returns its id.</summary>
    /// <param name="initial">The server-minted initial profile. Its player id is the identity created.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The created player's id — <paramref name="initial"/>'s own.</returns>
    /// <exception cref="InvalidOperationException">A row already exists for this id. Server-minted ids never collide, so this is a miswired caller, never a state to merge.</exception>
    Task<PlayerId> CreateAnonymousAsync(PlayerProfile initial, CancellationToken ct);
}
