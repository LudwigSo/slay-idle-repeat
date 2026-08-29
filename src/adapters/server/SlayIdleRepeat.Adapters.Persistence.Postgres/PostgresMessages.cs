using System.Text.Json;
using Npgsql;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Inbox;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.Persistence.Postgres;

/// <summary>The Postgres <see cref="IMessageRepository"/>: the <c>player_messages</c> rows.</summary>
/// <remarks>
/// <para>
/// Every read that answers a player is bounded by <c>(player_id, expires_at_utc)</c> — the index the
/// table ships with — and a row whose expiry is <c>NULL</c> never expires, which is what makes the
/// two record categories permanent without this adapter knowing which they are. Deciding that is
/// <c>MessageCategories.IsPermanentRecord</c>'s; this class stores rows.
/// </para>
/// <para>
/// 🔒 <see cref="MarkClaimedAsync"/> also exists in a caller-owned-transaction overload, exactly as
/// the player repository's save and the economy event log's append do, and <c>PostgresUnitOfWork</c>
/// calls it: an accepted claim's stamp lands inside the same transaction as the snapshot whose
/// wallet it paid into. The instance overload owns its own transaction and serves the callers that
/// have none — the expiry sweep's auto-grant.
/// </para>
/// </remarks>
public sealed class PostgresMessageRepository : IMessageRepository
{
    /// <summary>
    /// The stamp statement, named so a unit test can hold it against <c>0004_player_messages.sql</c>'s
    /// own column list without a database.
    /// </summary>
    /// <remarks>
    /// 🔒 <c>claimed_at_utc IS NULL</c> is the idempotence: a re-stamp keeps the first moment, which
    /// is the record of when the reward was actually paid. Without it a replayed claim would move
    /// the timestamp forward and an auditor reading the row would date the payment to the retry.
    /// </remarks>
    public const string MarkClaimedStatement =
        "UPDATE player_messages SET claimed_at_utc = @at " +
        "WHERE player_id = @player AND message_id = ANY(@ids) AND claimed_at_utc IS NULL;";

    private const string Columns =
        "message_id, player_id, category, template_id, params::text, attachments::text, " +
        "created_at_utc, expires_at_utc, read_at_utc, claimed_at_utc";

    private readonly NpgsqlDataSource _dataSource;

    internal PostgresMessageRepository(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PlayerMessage>> GetActiveAsync(PlayerId id, CancellationToken ct)
    {
        PostgresRows.RequireId(id.Value, nameof(id), "player");

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT " + Columns + " FROM player_messages " +
            "WHERE player_id = @player AND (expires_at_utc IS NULL OR expires_at_utc > now()) " +
            "ORDER BY created_at_utc, message_id;",
            connection);
        command.Parameters.AddWithValue("player", id.Value);

        return await ReadAllAsync(command, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task AppendAsync(PlayerMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        PostgresRows.RequireId(message.Player.Value, nameof(message), "player");
        PostgresRows.RequireId(message.Id.Value, nameof(message), "message");

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var insert = new NpgsqlCommand(
            "INSERT INTO player_messages " +
            "(message_id, player_id, category, template_id, params, attachments, created_at_utc, " +
            "expires_at_utc, read_at_utc, claimed_at_utc) " +
            "VALUES (@id, @player, @category, @template, @params, @attachments, @created, " +
            "@expires, @read, @claimed) " +
            // DO NOTHING is the port's idempotence: a retried segment send must leave the half that
            // landed exactly as it was, claimed stamp included.
            "ON CONFLICT (message_id) DO NOTHING;",
            connection);

        insert.Parameters.AddWithValue("id", message.Id.Value);
        insert.Parameters.AddWithValue("player", message.Player.Value);
        insert.Parameters.AddWithValue("category", message.Category.ToString());
        insert.Parameters.AddWithValue("template", message.TemplateId);
        insert.Parameters.Add(PostgresRows.JsonbOf("params", MessageJson.Params(message.Params)));
        insert.Parameters.Add(
            PostgresRows.JsonbOf("attachments", MessageJson.Attachments(message.Attachments)));
        insert.Parameters.AddWithValue("created", message.CreatedAtUtc);
        insert.Parameters.AddWithValue("expires", (object?)message.ExpiresAtUtc ?? DBNull.Value);
        insert.Parameters.AddWithValue("read", (object?)message.ReadAtUtc ?? DBNull.Value);
        insert.Parameters.AddWithValue("claimed", (object?)message.ClaimedAtUtc ?? DBNull.Value);

        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task MarkClaimedAsync(PlayerId id, IReadOnlyList<MessageId> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);
        PostgresRows.RequireId(id.Value, nameof(id), "player");

        if (ids.Count == 0)
        {
            return;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await MarkClaimedAsync(connection, transaction, id, ids, DateTimeOffset.UtcNow, ct)
            .ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The stamp on a caller-owned transaction — the shape the unit-of-work task composes.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="transaction">The transaction the stamp commits with.</param>
    /// <param name="id">Whose messages.</param>
    /// <param name="ids">The messages to stamp.</param>
    /// <param name="atUtc">The instant to record the payment at — the caller's clock, never this one's.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException">The connection, transaction or id list is null.</exception>
    public static async Task MarkClaimedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        PlayerId id,
        IReadOnlyList<MessageId> ids,
        DateTimeOffset atUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return;
        }

        await using var update = new NpgsqlCommand(MarkClaimedStatement, connection, transaction);

        update.Parameters.AddWithValue("at", atUtc);
        update.Parameters.AddWithValue("player", id.Value);
        update.Parameters.AddWithValue("ids", ids.Select(m => m.Value).ToArray());

        await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PlayerMessage>> DequeueExpiringAsync(
        DateTimeOffset asOfUtc, int limit, CancellationToken ct)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit), limit,
                "A batch of no messages answers 'nothing is due' for an inbox that is overdue, and " +
                "the job would report a clean sweep for ever.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT " + Columns + " FROM player_messages " +
            "WHERE expires_at_utc IS NOT NULL AND expires_at_utc <= @asOf " +
            "ORDER BY expires_at_utc, message_id LIMIT @limit;",
            connection);
        command.Parameters.AddWithValue("asOf", asOfUtc);
        command.Parameters.AddWithValue("limit", limit);

        return await ReadAllAsync(command, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(IReadOnlyList<MessageId> ids, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Count == 0)
        {
            return;
        }

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "DELETE FROM player_messages WHERE message_id = ANY(@ids);", connection);
        command.Parameters.AddWithValue("ids", ids.Select(m => m.Value).ToArray());

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<IReadOnlyList<PlayerMessage>> ReadAllAsync(
        NpgsqlCommand command, CancellationToken ct)
    {
        var rows = new List<PlayerMessage>();

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new PlayerMessage(
                new MessageId(reader.GetString(0)),
                new PlayerId(reader.GetString(1)),
                Enum.Parse<MessageCategory>(reader.GetString(2)),
                reader.GetString(3),
                MessageJson.ReadParams(reader.GetString(4)),
                MessageJson.ReadAttachments(reader.GetString(5)),
                reader.GetFieldValue<DateTimeOffset>(6),
                await NullableTimeAsync(reader, 7, ct).ConfigureAwait(false),
                await NullableTimeAsync(reader, 8, ct).ConfigureAwait(false),
                await NullableTimeAsync(reader, 9, ct).ConfigureAwait(false)));
        }

        return rows;
    }

    private static async Task<DateTimeOffset?> NullableTimeAsync(
        NpgsqlDataReader reader, int ordinal, CancellationToken ct) =>
        await reader.IsDBNullAsync(ordinal, ct).ConfigureAwait(false)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(ordinal);
}

/// <summary>
/// The two JSONB columns' encoding, in one place so the writer and the reader cannot part company.
/// </summary>
/// <remarks>
/// Plain <c>System.Text.Json</c> rather than the canonical snapshot codec: a message is never hashed
/// into a state hash and never rehydrated into an aggregate, so it needs a readable encoding rather
/// than a byte-stable one — and an operator reading the row in <c>psql</c> is a real use.
/// </remarks>
internal static class MessageJson
{
    /// <summary>The template parameters as a flat JSON object of strings.</summary>
    internal static string Params(IReadOnlyDictionary<string, string> parameters) =>
        JsonSerializer.Serialize(
            parameters.OrderBy(p => p.Key, StringComparer.Ordinal)
                      .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal));

    /// <summary>The attachments as the design set's own array of type-and-amount objects.</summary>
    internal static string Attachments(IReadOnlyList<MailAttachment> attachments) =>
        JsonSerializer.Serialize(
            attachments.Select(a => new StoredAttachment(a.Type, a.Amount)).ToArray());

    /// <summary>Reads the parameters column back.</summary>
    internal static IReadOnlyDictionary<string, string> ReadParams(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json)
        ?? throw new InvalidOperationException(
            "A player_messages row's params column decoded to null. A message renders from its " +
            "parameters, so a row with none is one the player would be shown the holes in.");

    /// <summary>Reads the attachments column back.</summary>
    internal static IReadOnlyList<MailAttachment> ReadAttachments(string json) =>
        (JsonSerializer.Deserialize<StoredAttachment[]>(json)
         ?? throw new InvalidOperationException(
             "A player_messages row's attachments column decoded to null. An empty array and a null " +
             "are different claims, and only the first is a message that carries nothing."))
        .Select(a => new MailAttachment(a.Type, a.Amount))
        .ToArray();

    private sealed record StoredAttachment(string Type, long Amount);
}

/// <summary>
/// The segment-send audit log. Not a port: a send is an operator action from a console tool, and
/// nothing in the game reads these rows — the same reason the economy event log is not one either.
/// </summary>
public sealed class PostgresMailSegmentAudit
{
    /// <summary>
    /// The append statement, named so a unit test can hold it against
    /// <c>0008_mail_segment_sends.sql</c>'s own column list without a database.
    /// </summary>
    public const string AppendStatement =
        "INSERT INTO mail_segment_sends " +
        "(sent_at_utc, operator, predicate, template_id, attachments, dry_run_count, actual_count) " +
        "VALUES (@at, @operator, @predicate, @template, @attachments, @dryRun, @actual);";

    private readonly NpgsqlDataSource _dataSource;

    internal PostgresMailSegmentAudit(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>Records one executed send.</summary>
    /// <param name="send">What was sent, by whom, to how many.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="send"/> is null.</exception>
    public async Task AppendAsync(MailSegmentSend send, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(send);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var insert = new NpgsqlCommand(AppendStatement, connection);

        insert.Parameters.AddWithValue("at", send.SentAtUtc);
        insert.Parameters.AddWithValue("operator", send.Operator);
        insert.Parameters.AddWithValue("predicate", send.Predicate);
        insert.Parameters.AddWithValue("template", send.TemplateId);
        insert.Parameters.AddWithValue("attachments", send.Attachments);
        insert.Parameters.AddWithValue("dryRun", send.DryRunCount);
        insert.Parameters.AddWithValue("actual", send.ActualCount);

        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
