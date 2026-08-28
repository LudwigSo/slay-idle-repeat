using Npgsql;
using SlayIdleRepeat.Application.Services.Content;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.Persistence.Postgres;

/// <summary>
/// The durable <see cref="IContentPinStore"/>: the run pin as a typed column beside the run's
/// snapshot, the session pin as a row of its own.
/// </summary>
/// <remarks>
/// <para>
/// The run pin rides the run row rather than a table of its own because it lives and dies with that
/// run: the row's sliding expiry is what stops a pin outliving the thing it pins, and a separate
/// table would need its own cleanup to say the same thing less reliably.
/// </para>
/// <para>
/// ⚠️ <b>Reads skip expired runs</b>, matching the run store's own rule that an expired row answers
/// as absent. A command on an expired run is refused above this layer, so a pin read that ignored
/// the expiry would only ever resolve a snapshot nobody was going to use.
/// </para>
/// <para>
/// 🔴 <b>No in-repo fixture exercises this class</b>, and it carries no entry in the store-backed
/// adapter register either: that register quantifies over implementations of PORTS, and
/// <see cref="IContentPinStore"/> is an application seam rather than a port, so an entry for this
/// type would fail the register's own anchoring rule. Its only backing is a live Postgres. The
/// column names below and <c>0005_content_pinning.sql</c> are two halves of one row with nothing
/// between them; keep them in the same commit.
/// </para>
/// </remarks>
public sealed class PostgresContentPinStore : IContentPinStore
{
    private readonly NpgsqlDataSource _dataSource;

    internal PostgresContentPinStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <inheritdoc/>
    public async Task<ContentVersion?> ReadRunPinAsync(RunId run, CancellationToken ct)
    {
        PostgresRows.RequireId(run.Value, nameof(run), "run");

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT content_version FROM runs WHERE run_id = @id AND expires_at_utc > now();", connection);
        command.Parameters.AddWithValue("id", run.Value);

        return Parse(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public async Task WriteRunPinAsync(
        RunId run, ContentVersion version, DateTimeOffset atUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(version);
        PostgresRows.RequireId(run.Value, nameof(run), "run");

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        // Only where it is still unset: a run's pin is taken once, when the run opens, and a second
        // write would be a run judged against two content sets across its own lifetime.
        await using var command = new NpgsqlCommand(
            "UPDATE runs SET content_version = @version " +
            "WHERE run_id = @id AND content_version IS NULL;", connection);
        command.Parameters.AddWithValue("id", run.Value);
        command.Parameters.AddWithValue("version", version.Value);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<ContentVersion?> ReadSessionPinAsync(PlayerId player, CancellationToken ct)
    {
        PostgresRows.RequireId(player.Value, nameof(player), "player");

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT content_version FROM content_session_pins WHERE player_id = @id;", connection);
        command.Parameters.AddWithValue("id", player.Value);

        return Parse(await command.ExecuteScalarAsync(ct).ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public async Task WriteSessionPinAsync(
        PlayerId player, ContentVersion version, DateTimeOffset atUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(version);
        PostgresRows.RequireId(player.Value, nameof(player), "player");

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "INSERT INTO content_session_pins (player_id, content_version, pinned_at_utc, last_seen_at_utc) " +
            "VALUES (@id, @version, @at, @at) " +
            "ON CONFLICT (player_id) DO UPDATE SET " +
            "content_version = EXCLUDED.content_version, " +
            "pinned_at_utc = EXCLUDED.pinned_at_utc, " +
            "last_seen_at_utc = EXCLUDED.last_seen_at_utc;", connection);
        command.Parameters.AddWithValue("id", player.Value);
        command.Parameters.AddWithValue("version", version.Value);
        command.Parameters.AddWithValue("at", atUtc.UtcDateTime);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RetainedVersion>> ListRetainedAsync(CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        // Both pin kinds in one answer, reduced to the latest reference per version: a sweep asks
        // "what is still wanted, and how recently", and two rows for one stamp would let it delete
        // a bundle the newer of the two still needs. Expired runs drop out — an expired run cannot
        // take another command, so it holds nothing.
        await using var command = new NpgsqlCommand(
            "SELECT version, max(seen) FROM (" +
            "  SELECT content_version AS version, last_applied_at_utc AS seen FROM runs " +
            "  WHERE content_version IS NOT NULL AND expires_at_utc > now()" +
            "  UNION ALL" +
            "  SELECT content_version AS version, last_seen_at_utc AS seen FROM content_session_pins" +
            ") AS pins GROUP BY version ORDER BY version;", connection);

        var retained = new List<RetainedVersion>();

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (ContentVersion.TryFromHex(reader.GetString(0), out var version))
            {
                retained.Add(new RetainedVersion(
                    version!, new DateTimeOffset(reader.GetDateTime(1), TimeSpan.Zero)));
            }
        }

        return retained;
    }

    /// <summary>A stored stamp, or <c>null</c> for absent, SQL NULL, or text that is not a stamp.</summary>
    /// <remarks>
    /// Unparseable text answers "no pin" rather than throwing: a column somebody hand-edited must
    /// degrade to the loud current-version fallback, not fault every command on that run.
    /// </remarks>
    private static ContentVersion? Parse(object? stored) =>
        stored is string text && ContentVersion.TryFromHex(text, out var version) ? version : null;
}
