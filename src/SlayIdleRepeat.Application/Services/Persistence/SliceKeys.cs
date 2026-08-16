using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Application.Services.Persistence;

/// <summary>
/// The two keys this layer stores state under, and the only place their spelling is decided.
/// </summary>
/// <remarks>
/// <para>
/// One committed row per player (<c>player.&lt;playerId&gt;</c>) holding the player and whatever run
/// the player is in, plus one archived row per finished run (<c>run.&lt;runId&gt;</c>). The player
/// row is the commit point: player and run move under one key, in one write, so a crash can never
/// tear one from the other.
/// </para>
/// <para>
/// Both methods refuse an id whose text would leave <see cref="ILocalCachePort"/>'s closed key space
/// rather than escaping or truncating it. Two different ids that mangled to the same key would read
/// each other's state, and an id that mangled to a key the file-backed store rejects would work in
/// memory and fail on a device.
/// </para>
/// </remarks>
public static class SliceKeys
{
    /// <summary>The key a player's committed slice is stored under.</summary>
    /// <param name="player">The player. Its text must lie inside the cache's key space.</param>
    /// <returns><c>"player."</c> followed by the id's text.</returns>
    /// <exception cref="ArgumentException">
    /// The id's text is empty, or holds a character outside <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>,
    /// <c>.</c>, <c>_</c> and <c>-</c>.
    /// </exception>
    public static string ForPlayer(PlayerId player) => throw new NotImplementedException();

    /// <summary>The key a finished run is archived under.</summary>
    /// <param name="run">The run. Its text must lie inside the cache's key space.</param>
    /// <returns><c>"run."</c> followed by the id's text.</returns>
    /// <exception cref="ArgumentException">
    /// The id's text is empty, or holds a character outside <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>,
    /// <c>.</c>, <c>_</c> and <c>-</c>.
    /// </exception>
    public static string ForRun(RunId run) => throw new NotImplementedException();
}
