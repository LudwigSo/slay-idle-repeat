using System.Text;
using Npgsql;
using NpgsqlTypes;
using SlayIdleRepeat.Application.Ports.Server;
using SlayIdleRepeat.Application.Services.Events;
using SlayIdleRepeat.Application.Services.Persistence;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Model.Snapshots;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Adapters.Persistence.Postgres;

/// <summary>The one door into this adapter: a connection string in, the stores and the migration runner out.</summary>
/// <remarks>
/// The composition root hands this a connection string and a run lifetime and never sees an Npgsql
/// type: the vendor package is referenced by this project and no other, so the factory takes
/// primitives. The lifetime is here because the player repository restamps the active run's sliding
/// window on every save — "measured from the last accepted command" is this adapter's to keep true.
/// </remarks>
public sealed class PostgresPersistence : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    private PostgresPersistence(NpgsqlDataSource dataSource, TimeSpan runTtl)
    {
        _dataSource = dataSource;
        Players = new PostgresPlayerRepository(dataSource, runTtl);
        RunStates = new PostgresRunStateStore(dataSource);
        Idempotency = new PostgresIdempotencyStore(dataSource);
        EconomyEvents = new PostgresEconomyEventLog(dataSource);
    }

    /// <summary>Opens the adapter over one pooled data source.</summary>
    /// <param name="connectionString">The Postgres connection string.</param>
    /// <param name="runTtl">The run rows' sliding lifetime — the configured 48 h window. Positive.</param>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is null or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="runTtl"/> is zero or negative.</exception>
    public static PostgresPersistence Create(string connectionString, TimeSpan runTtl)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "A blank connection string is a deployment that forgot ConnectionStrings:Postgres — " +
                "fail here, where the missing variable is nameable.",
                nameof(connectionString));
        }

        if (runTtl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(runTtl), runTtl, "A non-positive run lifetime expires every run at birth.");
        }

        return new PostgresPersistence(NpgsqlDataSource.Create(connectionString), runTtl);
    }

    /// <summary>The player-profile store.</summary>
    public IPlayerRepository Players { get; }

    /// <summary>The run-row store — the authoritative layer a cache decorates.</summary>
    public IRunStateStore RunStates { get; }

    /// <summary>The sequencing and idempotency store.</summary>
    public IIdempotencyStore Idempotency { get; }

    /// <summary>The append-only economy event log.</summary>
    public PostgresEconomyEventLog EconomyEvents { get; }

    /// <summary>Applies every pending migration, once per cluster, under the advisory lock.</summary>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="InvalidOperationException">An applied file's checksum no longer matches its stored one — the history was edited in place.</exception>
    public async Task MigrateAsync(CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        // The lock is session-scoped: two API instances starting together serialize here, and the
        // second finds everything applied. Released explicitly so a pooled session cannot carry it.
        await ExecuteAsync(connection, "SELECT pg_advisory_lock(" + MigrationPlan.AdvisoryLockKey + ");", ct)
            .ConfigureAwait(false);

        try
        {
            await ExecuteAsync(connection, "CREATE SCHEMA IF NOT EXISTS meta;", ct).ConfigureAwait(false);
            await ExecuteAsync(
                    connection,
                    "CREATE TABLE IF NOT EXISTS meta.migrations (" +
                    "file_name text PRIMARY KEY, checksum text NOT NULL, " +
                    "applied_at_utc timestamptz NOT NULL DEFAULT now());",
                    ct)
                .ConfigureAwait(false);

            foreach (var script in PostgresMigrations.All())
            {
                await ApplyIfPendingAsync(connection, script, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            await ExecuteAsync(
                    connection, "SELECT pg_advisory_unlock(" + MigrationPlan.AdvisoryLockKey + ");",
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    private static async Task ApplyIfPendingAsync(
        NpgsqlConnection connection, MigrationScript script, CancellationToken ct)
    {
        var checksum = MigrationPlan.ChecksumOf(script.Sql);

        await using (var read = new NpgsqlCommand(
                         "SELECT checksum FROM meta.migrations WHERE file_name = @file;", connection))
        {
            read.Parameters.AddWithValue("file", script.FileName);

            if (await read.ExecuteScalarAsync(ct).ConfigureAwait(false) is string applied)
            {
                if (applied != checksum)
                {
                    throw new InvalidOperationException(
                        "'" + script.FileName + "' was applied with checksum " + applied + " and the " +
                        "embedded file now hashes to " + checksum + ". An applied migration is " +
                        "history: databases that ran the old text can never converge with ones that " +
                        "run the new one. Add the change as the next numbered file instead.");
                }

                return;
            }
        }

        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var apply = new NpgsqlCommand(script.Sql, connection, transaction))
        {
            await apply.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var record = new NpgsqlCommand(
                         "INSERT INTO meta.migrations (file_name, checksum) VALUES (@file, @checksum);",
                         connection, transaction))
        {
            record.Parameters.AddWithValue("file", script.FileName);
            record.Parameters.AddWithValue("checksum", checksum);
            await record.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>The Postgres <see cref="IPlayerRepository"/>: the players row (JSONB document + typed columns) and, with it, the run's row.</summary>
public sealed class PostgresPlayerRepository : IPlayerRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly TimeSpan _runTtl;

    internal PostgresPlayerRepository(NpgsqlDataSource dataSource, TimeSpan runTtl)
    {
        _dataSource = dataSource;
        _runTtl = runTtl;
    }

    /// <inheritdoc/>
    public async Task<PlayerProfile?> GetAsync(PlayerId id, CancellationToken ct)
    {
        PostgresRows.RequireId(id.Value, nameof(id), "player");

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT doc FROM players WHERE player_id = @id;", connection);
        command.Parameters.AddWithValue("id", id.Value);

        if (await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is not string doc)
        {
            return null;
        }

        var stored = SnapshotCodec.DecodeSlice(Encoding.UTF8.GetBytes(doc));

        return new PlayerProfile(stored.Player, stored.Run);
    }

    /// <inheritdoc/>
    public async Task SaveAsync(PlayerProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(profile);
        PostgresRows.RequireId(profile.Player.Id.Value, nameof(profile), "player");

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await SaveAsync(connection, transaction, profile, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The save on a caller-owned transaction — the shape the unit-of-work task composes.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="transaction">The transaction the save commits with.</param>
    /// <param name="profile">The state to commit.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task SaveAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, PlayerProfile profile, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(profile);
        PostgresRows.RequireId(profile.Player.Id.Value, nameof(profile), "player");

        await using (var upsert = new NpgsqlCommand(
                         "INSERT INTO players (player_id, doc, display_name, legend_level, last_applied_at_utc) " +
                         "VALUES (@id, @doc, @name, @level, @applied) " +
                         "ON CONFLICT (player_id) DO UPDATE SET " +
                         "doc = EXCLUDED.doc, display_name = EXCLUDED.display_name, " +
                         "legend_level = EXCLUDED.legend_level, " +
                         "last_applied_at_utc = EXCLUDED.last_applied_at_utc, updated_at_utc = now();",
                         connection, transaction))
        {
            upsert.Parameters.AddWithValue("id", profile.Player.Id.Value);
            upsert.Parameters.Add(PostgresRows.JsonbOf("doc", PostgresRows.SliceDocOf(profile)));
            upsert.Parameters.AddWithValue("name", profile.Player.DisplayName);
            upsert.Parameters.AddWithValue("level", profile.Player.LegendLevel);
            upsert.Parameters.AddWithValue("applied", profile.Player.LastAppliedAtUtc);

            await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (profile.ActiveRun is { } run)
        {
            // The active run's row moves with the player's, in this same transaction, and the save
            // restamps its sliding lifetime — measured from the last accepted command.
            await PostgresRows.UpsertRunAsync(connection, transaction, run, _runTtl, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task<PlayerId> CreateAnonymousAsync(PlayerProfile initial, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(initial);
        PostgresRows.RequireId(initial.Player.Id.Value, nameof(initial), "player");

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var insert = new NpgsqlCommand(
                         "INSERT INTO players (player_id, doc, display_name, legend_level, last_applied_at_utc) " +
                         "VALUES (@id, @doc, @name, @level, @applied) " +
                         "ON CONFLICT (player_id) DO NOTHING;",
                         connection, transaction))
        {
            insert.Parameters.AddWithValue("id", initial.Player.Id.Value);
            insert.Parameters.Add(PostgresRows.JsonbOf("doc", PostgresRows.SliceDocOf(initial)));
            insert.Parameters.AddWithValue("name", initial.Player.DisplayName);
            insert.Parameters.AddWithValue("level", initial.Player.LegendLevel);
            insert.Parameters.AddWithValue("applied", initial.Player.LastAppliedAtUtc);

            if (await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            {
                throw new InvalidOperationException(
                    "A row already exists for player " + initial.Player.Id + ". Server-minted ids " +
                    "never collide, so this is a miswired caller — overwriting would hand one " +
                    "player another's account.");
            }
        }

        if (initial.ActiveRun is { } run)
        {
            await PostgresRows.UpsertRunAsync(connection, transaction, run, _runTtl, ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);

        return initial.Player.Id;
    }
}

/// <summary>The Postgres <see cref="IRunStateStore"/>: the runs row, with expiry enforced at read.</summary>
public sealed class PostgresRunStateStore : IRunStateStore
{
    private readonly NpgsqlDataSource _dataSource;

    internal PostgresRunStateStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <inheritdoc/>
    public async Task<RunSnapshot?> GetAsync(RunId id, CancellationToken ct)
    {
        PostgresRows.RequireId(id.Value, nameof(id), "run");

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(
            "SELECT doc FROM runs WHERE run_id = @id AND expires_at_utc > now();", connection);
        command.Parameters.AddWithValue("id", id.Value);

        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is string doc
            ? SnapshotCodec.DecodeRun(Encoding.UTF8.GetBytes(doc))
            : null;
    }

    /// <inheritdoc/>
    public async Task SaveAsync(RunSnapshot state, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(state);
        PostgresRows.RequireId(state.Id.Value, nameof(state), "run");

        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ttl), ttl, "A row born expired is a delete spelled confusingly.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await PostgresRows.UpsertRunAsync(connection, transaction, state, ttl, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(RunId id, CancellationToken ct)
    {
        PostgresRows.RequireId(id.Value, nameof(id), "run");

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("DELETE FROM runs WHERE run_id = @id;", connection);
        command.Parameters.AddWithValue("id", id.Value);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>The Postgres <see cref="IIdempotencyStore"/>: records in their own table, counters on the player and run rows.</summary>
public sealed class PostgresIdempotencyStore : IIdempotencyStore
{
    private readonly NpgsqlDataSource _dataSource;

    internal PostgresIdempotencyStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <inheritdoc/>
    public async Task<RecordedCommandOutcome?> GetRecordedOutcomeAsync(
        IdempotencyScope scope, CommandId commandId, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        // A player record carries its own expiry; a run record lives exactly as long as its run —
        // "a resumable run must be able to replay any of its outcomes" — so its liveness is the
        // run row's, joined here rather than copied and drifting.
        var sql = scope.Kind == IdempotencyScopeKind.Player
            ? "SELECT ir.command_id, ir.sequence, ir.command_type, ir.payload::text, ir.response_body, ir.opens_scope " +
              "FROM idempotency_records ir " +
              "WHERE ir.scope_kind = 'player' AND ir.scope_key = @key AND ir.command_id = @command " +
              "AND ir.expires_at_utc > now();"
            : "SELECT ir.command_id, ir.sequence, ir.command_type, ir.payload::text, ir.response_body, ir.opens_scope " +
              "FROM idempotency_records ir JOIN runs r ON r.run_id = ir.run_id " +
              "WHERE ir.scope_kind = 'run' AND ir.scope_key = @key AND ir.command_id = @command " +
              "AND r.expires_at_utc > now();";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("key", PostgresRows.ScopeKeyOf(scope));
        command.Parameters.AddWithValue("command", commandId.Value);

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        return new RecordedCommandOutcome(
            new CommandId(reader.GetString(0)),
            reader.GetInt64(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            await reader.IsDBNullAsync(5, ct).ConfigureAwait(false) ? null : reader.GetString(5));
    }

    /// <inheritdoc/>
    public async Task RecordAsync(
        IdempotencyScope scope, RecordedCommandOutcome outcome, TimeSpan ttl, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        if (ttl <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ttl), ttl, "A record born expired turns its command's next retry into a double-apply.");
        }

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await RecordAsync(connection, transaction, scope, outcome, ttl, ct).ConfigureAwait(false);

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The record on a caller-owned transaction — the shape the unit-of-work task composes.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="transaction">The transaction the record commits with.</param>
    /// <param name="scope">The sequencing domain.</param>
    /// <param name="outcome">The record.</param>
    /// <param name="ttl">The record's lifetime — player-kind only; a run record's liveness is its run's.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task RecordAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IdempotencyScope scope,
        RecordedCommandOutcome outcome,
        TimeSpan ttl,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(outcome);

        await using (var insert = new NpgsqlCommand(
                         "INSERT INTO idempotency_records " +
                         "(scope_kind, scope_key, player_id, run_id, command_id, sequence, command_type, " +
                         "payload, response_body, opens_scope, expires_at_utc) " +
                         "VALUES (@kind, @key, @player, @run, @command, @sequence, @type, @payload, " +
                         "@response, @opens, CASE WHEN @kind = 'player' THEN now() + @ttl ELSE NULL END);",
                         connection, transaction))
        {
            insert.Parameters.AddWithValue("kind", scope.Kind == IdempotencyScopeKind.Run ? "run" : "player");
            insert.Parameters.AddWithValue("key", PostgresRows.ScopeKeyOf(scope));
            insert.Parameters.AddWithValue("player", scope.Player.Value);
            insert.Parameters.AddWithValue("run", (object?)scope.Run?.Value ?? DBNull.Value);
            insert.Parameters.AddWithValue("command", outcome.CommandId.Value);
            insert.Parameters.AddWithValue("sequence", outcome.Sequence);
            insert.Parameters.AddWithValue("type", outcome.CommandType);
            insert.Parameters.Add(PostgresRows.JsonbOf("payload", outcome.PayloadJson));
            insert.Parameters.AddWithValue("response", outcome.ResponseBody);
            insert.Parameters.AddWithValue("opens", (object?)outcome.OpensScope ?? DBNull.Value);
            insert.Parameters.AddWithValue("ttl", ttl);

            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // The advance, in the same transaction — the port's one atomic effect. GREATEST keeps a
        // replayed lower sequence from ever winding a counter back.
        var advance = scope.Kind == IdempotencyScopeKind.Run
            ? "UPDATE runs SET last_sequence = GREATEST(COALESCE(last_sequence, 0), @sequence) WHERE run_id = @id;"
            : "UPDATE players SET last_meta_sequence = GREATEST(COALESCE(last_meta_sequence, 0), @sequence) " +
              "WHERE player_id = @id;";

        await using (var update = new NpgsqlCommand(advance, connection, transaction))
        {
            update.Parameters.AddWithValue("sequence", outcome.Sequence);
            update.Parameters.AddWithValue(
                "id", scope.Kind == IdempotencyScopeKind.Run ? scope.Run!.Value.Value : scope.Player.Value);

            if (await update.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            {
                throw new InvalidOperationException(
                    "No row exists to carry the counter of " + PostgresRows.ScopeKeyOf(scope) + ". " +
                    "The port's contract presumes the scope's subject was saved before its record " +
                    "(the accepted command's own save runs first), so a record for a subject this " +
                    "store has never seen is a miswired caller.");
            }
        }
    }

    /// <inheritdoc/>
    public async Task<long?> ReadLastSequenceAsync(IdempotencyScope scope, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        var sql = scope.Kind == IdempotencyScopeKind.Run
            ? "SELECT last_sequence FROM runs WHERE run_id = @id AND expires_at_utc > now();"
            : "SELECT last_meta_sequence FROM players WHERE player_id = @id;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(
            "id", scope.Kind == IdempotencyScopeKind.Run ? scope.Run!.Value.Value : scope.Player.Value);

        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false) is long last ? last : null;
    }

    /// <inheritdoc/>
    public async Task OpenScopeAsync(IdempotencyScope scope, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        // COALESCE is the whole of "idempotent, never a reset".
        var sql = scope.Kind == IdempotencyScopeKind.Run
            ? "UPDATE runs SET last_sequence = COALESCE(last_sequence, 0) WHERE run_id = @id;"
            : "UPDATE players SET last_meta_sequence = COALESCE(last_meta_sequence, 0) WHERE player_id = @id;";

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue(
            "id", scope.Kind == IdempotencyScopeKind.Run ? scope.Run!.Value.Value : scope.Player.Value);

        if (await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
        {
            throw new InvalidOperationException(
                "No row exists to open the scope " + PostgresRows.ScopeKeyOf(scope) + " on. The " +
                "accepted command's save precedes its open, so a missing subject is a miswired caller.");
        }
    }
}

/// <summary>The append-only economy event log. Not a port: the appends belong inside the commit transaction, whose composition is the unit-of-work task's.</summary>
public sealed class PostgresEconomyEventLog
{
    /// <summary>
    /// The append statement, named so a unit test can hold it against
    /// <c>0003_economy_events.sql</c>'s own column list.
    /// </summary>
    /// <remarks>
    /// 🔒 This class has no production caller yet — the appends ride inside the accepted command's
    /// one transaction, and that composition is the unit-of-work task's — so nothing else would
    /// notice a column renamed on one side of the pair. Exposed rather than inlined for exactly
    /// that reason (steering S25): the drift is what a live database would catch, and the column
    /// pin is what can catch it without one.
    /// </remarks>
    public const string AppendStatement =
        "INSERT INTO economy_events " +
        "(player_id, run_id, command_id, sequence, occurred_at_utc, event_type, payload) " +
        "VALUES (@player, @run, @command, @sequence, @occurred, @type, @payload);";

    private readonly NpgsqlDataSource _dataSource;

    internal PostgresEconomyEventLog(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>Appends one command's enriched events on a caller-owned transaction — the shape the unit-of-work task composes.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="transaction">The transaction the append commits with.</param>
    /// <param name="records">The enriched rows, in event order.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task AppendAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<EconomyEventRecord> records,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(records);

        if (records.Count == 0)
        {
            return;
        }

        // One round-trip for the whole command's list: the appends ride inside the commit
        // transaction, and a per-event round-trip would stretch it for nothing.
        await using var batch = new NpgsqlBatch(connection, transaction);

        foreach (var record in records)
        {
            var insert = new NpgsqlBatchCommand(AppendStatement);

            insert.Parameters.AddWithValue("player", record.Player.Value);
            insert.Parameters.AddWithValue("run", (object?)record.Run?.Value ?? DBNull.Value);
            insert.Parameters.AddWithValue("command", record.CommandId.Value);
            insert.Parameters.AddWithValue("sequence", record.Sequence);
            insert.Parameters.AddWithValue("occurred", record.OccurredAtUtc);
            insert.Parameters.AddWithValue("type", record.EventType);
            insert.Parameters.Add(PostgresRows.JsonbOf("payload", record.PayloadJson));

            batch.BatchCommands.Add(insert);
        }

        await batch.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>This adapter's shared row plumbing: document text, scope keys, jsonb parameters, id guards.</summary>
internal static class PostgresRows
{
    /// <summary>The players row's document: the canonical stored slice, exactly as the codec writes it.</summary>
    internal static string SliceDocOf(PlayerProfile profile) =>
        Encoding.UTF8.GetString(SnapshotCodec.EncodeSlice(new StoredSlice(profile.Player, profile.ActiveRun)));

    /// <summary>The scope_key column's text: the player id, or player:run for a run scope.</summary>
    internal static string ScopeKeyOf(IdempotencyScope scope) =>
        scope.Kind == IdempotencyScopeKind.Run
            ? scope.Player.Value + ":" + scope.Run!.Value.Value
            : scope.Player.Value;

    /// <summary>A jsonb parameter, spelled once.</summary>
    internal static NpgsqlParameter JsonbOf(string name, string json) =>
        new(name, NpgsqlDbType.Jsonb) { Value = json };

    /// <summary>Refuses a default id before it can become a key.</summary>
    internal static void RequireId(string? value, string parameterName, string subject)
    {
        if (value is null)
        {
            throw new ArgumentException(
                "This " + subject + " id carries no text (a default struct) — a row keyed on it " +
                "would pool every such caller's state into one row.",
                parameterName);
        }
    }

    /// <summary>Upserts one run's row and restamps its sliding lifetime.</summary>
    internal static async Task UpsertRunAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        RunSnapshot run,
        TimeSpan ttl,
        CancellationToken ct)
    {
        RequireId(run.Id.Value, nameof(run), "run");

        await using var upsert = new NpgsqlCommand(
            "INSERT INTO runs (run_id, player_id, doc, phase, chapter_id, tier, last_applied_at_utc, expires_at_utc) " +
            "VALUES (@id, @player, @doc, @phase, @chapter, @tier, @applied, now() + @ttl) " +
            "ON CONFLICT (run_id) DO UPDATE SET " +
            "doc = EXCLUDED.doc, phase = EXCLUDED.phase, chapter_id = EXCLUDED.chapter_id, " +
            "tier = EXCLUDED.tier, last_applied_at_utc = EXCLUDED.last_applied_at_utc, " +
            "expires_at_utc = EXCLUDED.expires_at_utc, updated_at_utc = now();",
            connection, transaction);

        upsert.Parameters.AddWithValue("id", run.Id.Value);
        upsert.Parameters.AddWithValue("player", run.PlayerId.Value);
        upsert.Parameters.Add(JsonbOf("doc", Encoding.UTF8.GetString(SnapshotCodec.EncodeRun(run))));
        upsert.Parameters.AddWithValue("phase", run.Phase.ToString());
        upsert.Parameters.AddWithValue("chapter", run.ChapterId);
        upsert.Parameters.AddWithValue("tier", run.Tier.ToString());
        upsert.Parameters.AddWithValue("applied", run.LastAppliedAtUtc);
        upsert.Parameters.AddWithValue("ttl", ttl);

        await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
