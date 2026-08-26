using SlayIdleRepeat.Application.Ports.Client;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// The byte store <c>WorldSliceStore</c> runs over, routed to the persistence ports: the player row
/// through <see cref="IPlayerRepository"/>, the archived run through <see cref="IRunStateStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// Composition-root plumbing, not an adapter: it exists so the store of record is reached ONLY
/// through its ports while the application's slice store keeps its byte seam until the unit-of-work
/// task re-plumbs the call site. It therefore honours exactly the two key prefixes
/// <c>SliceKeys</c> spells — <c>player.</c> and <c>run.</c> — and refuses anything else loudly:
/// this class is not a general cache, and a third prefix arriving here is a caller wired to the
/// wrong store.
/// </para>
/// <para>
/// A write of a player key decodes the slice and commits it through the repository — the player row
/// and its inline run's row in one transaction, exactly what the byte seam's single-key write meant.
/// A write of a run key is the finished-run archive: the row restamped with the configured lifetime.
/// </para>
/// </remarks>
public sealed class RepositoryWorldRows : ILocalCachePort
{
    private const string PlayerPrefix = "player.";
    private const string RunPrefix = "run.";

    private readonly IPlayerRepository _players;
    private readonly IRunStateStore _runs;
    private readonly TimeSpan _runTtl;

    /// <summary>Builds the bridge over the two ports.</summary>
    /// <param name="players">The player-profile store.</param>
    /// <param name="runs">The run-row store.</param>
    /// <param name="runTtl">The archived run's lifetime — the configured 48 h window. Positive.</param>
    /// <exception cref="ArgumentNullException">A port is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="runTtl"/> is zero or negative.</exception>
    public RepositoryWorldRows(IPlayerRepository players, IRunStateStore runs, TimeSpan runTtl)
    {
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(runs);

        if (runTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(runTtl), runTtl, "A non-positive run lifetime expires every archive at birth.");
        }

        _players = players;
        _runs = runs;
        _runTtl = runTtl;
    }

    /// <inheritdoc/>
    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (TryIdOf(key, PlayerPrefix) is { } playerId)
        {
            var profile = await _players.GetAsync(new PlayerId(playerId), ct).ConfigureAwait(false);

            return profile is null
                ? null
                : SnapshotCodec.EncodeSlice(new StoredSlice(profile.Player, profile.ActiveRun));
        }

        if (TryIdOf(key, RunPrefix) is { } runId)
        {
            var run = await _runs.GetAsync(new RunId(runId), ct).ConfigureAwait(false);

            return run is null ? null : SnapshotCodec.EncodeRun(run);
        }

        throw OutsideVocabulary(key);
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string key, ReadOnlyMemory<byte> value, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (TryIdOf(key, PlayerPrefix) is not null)
        {
            var stored = SnapshotCodec.DecodeSlice(value.Span);

            await _players.SaveAsync(new PlayerProfile(stored.Player, stored.Run), ct).ConfigureAwait(false);

            return;
        }

        if (TryIdOf(key, RunPrefix) is not null)
        {
            await _runs.SaveAsync(SnapshotCodec.DecodeRun(value.Span), _runTtl, ct).ConfigureAwait(false);

            return;
        }

        throw OutsideVocabulary(key);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (TryIdOf(key, RunPrefix) is { } runId)
        {
            await _runs.DeleteAsync(new RunId(runId), ct).ConfigureAwait(false);

            return;
        }

        // Nothing in the world store deletes a player, and account deletion is its own governed
        // flow — a delete arriving here would be that flow reaching the wrong door.
        throw OutsideVocabulary(key);
    }

    private static string? TryIdOf(string key, string prefix) =>
        key.StartsWith(prefix, StringComparison.Ordinal) && key.Length > prefix.Length
            ? key[prefix.Length..]
            : null;

    private static InvalidOperationException OutsideVocabulary(string key) =>
        new(
            "'" + key + "' is outside this store's key vocabulary (SliceKeys' 'player.<id>' and " +
            "'run.<id>', deletes for runs only). This bridge routes the world rows to the " +
            "persistence ports and nothing else; a caller with a different key belongs on a " +
            "different store.");
}
