using Npgsql;

namespace SlayIdleRepeat.Adapters.Persistence.Postgres;

/// <summary>
/// The auth rows over PostgreSQL: devices, token families, refresh tokens and the deletion record.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Deliberately NOT a port implementation, following <c>PostgresEconomyEventLog</c>: the seam the
/// composition root wraps this in is declared there, next to the wiring that owns it, and this class
/// stays a plain vendor-speaking type. A second real implementation would change that answer.
/// </para>
/// <para>
/// Every statement is a named constant because nothing in this repository's tiers can execute one:
/// there is no database in the unit tier, so a column renamed on one side of the schema/SQL pair
/// would sit undetected until a live run. The constants are what a unit test can pin against the
/// migration's own text.
/// </para>
/// </remarks>
public sealed class PostgresAuthStore
{
    /// <summary>Writes one device row — the anonymous account's root credential, hashed.</summary>
    public const string InsertDeviceStatement =
        "INSERT INTO auth_devices " +
        "(device_id, player_id, secret_hash, created_at_utc, last_seen_at_utc) " +
        "VALUES (@device, @player, @secret, @created, @seen);";

    /// <summary>Reads one device row by its id.</summary>
    public const string SelectDeviceStatement =
        "SELECT device_id, player_id, secret_hash, created_at_utc, last_seen_at_utc " +
        "FROM auth_devices WHERE device_id = @device;";

    /// <summary>Opens one token family — one per successful device-secret authentication.</summary>
    public const string InsertFamilyStatement =
        "INSERT INTO auth_token_families " +
        "(family_id, device_id, player_id, created_at_utc, revoked_at_utc, revoked_reason) " +
        "VALUES (@family, @device, @player, @created, @revoked, @reason);";

    /// <summary>Closes one family. Idempotent by the NULL guard: a second revocation keeps the first reason.</summary>
    public const string RevokeFamilyStatement =
        "UPDATE auth_token_families SET revoked_at_utc = @revoked, revoked_reason = @reason " +
        "WHERE family_id = @family AND revoked_at_utc IS NULL;";

    /// <summary>Writes one refresh token, as its digest.</summary>
    public const string InsertRefreshTokenStatement =
        "INSERT INTO auth_refresh_tokens " +
        "(token_hash, family_id, device_id, player_id, issued_at_utc, expires_at_utc, rotated_at_utc) " +
        "VALUES (@hash, @family, @device, @player, @issued, @expires, @rotated);";

    /// <summary>Marks one token rotated. The NULL guard is what makes rotation single-use under a race.</summary>
    public const string RotateRefreshTokenStatement =
        "UPDATE auth_refresh_tokens SET rotated_at_utc = @rotated " +
        "WHERE token_hash = @hash AND rotated_at_utc IS NULL;";

    /// <summary>Records the soft delete and the date the sweep must finish by.</summary>
    public const string InsertAccountDeletionStatement =
        "INSERT INTO auth_account_deletions " +
        "(player_id, requested_at_utc, hard_delete_due_at_utc) " +
        "VALUES (@player, @requested, @due);";

    /// <summary>Writes the anonymous tombstone the hard delete leaves behind.</summary>
    public const string InsertDeletionTombstoneStatement =
        "INSERT INTO auth_deletion_tombstones " +
        "(player_digest, requested_at_utc, hard_deleted_at_utc) " +
        "VALUES (@digest, @requested, @deleted);";

    /// <summary>Reads the device row a deletion re-confirmation is checked against.</summary>
    public const string SelectDeviceForPlayerStatement =
        "SELECT device_id, player_id, secret_hash, created_at_utc, last_seen_at_utc " +
        "FROM auth_devices WHERE player_id = @player LIMIT 1;";

    /// <summary>Restamps the device's authoritative last-seen instant.</summary>
    public const string TouchDeviceStatement =
        "UPDATE auth_devices SET last_seen_at_utc = @seen WHERE device_id = @device;";

    /// <summary>Whether this account carries a deletion record.</summary>
    public const string SelectAccountDeletionStatement =
        "SELECT player_id FROM auth_account_deletions WHERE player_id = @player;";

    /// <summary>Every soft-deleted account — what the request-path status view is built from.</summary>
    public const string SelectDeletedPlayersStatement =
        "SELECT player_id FROM auth_account_deletions;";

    /// <summary>The family one presented token digest belongs to.</summary>
    public const string SelectFamilyForTokenStatement =
        "SELECT f.family_id, f.device_id, f.player_id, f.created_at_utc, f.revoked_at_utc, f.revoked_reason " +
        "FROM auth_token_families f JOIN auth_refresh_tokens t ON t.family_id = f.family_id " +
        "WHERE t.token_hash = @hash;";

    /// <summary>Every token in one family — the rows a reuse revocation has to cover.</summary>
    public const string SelectTokensInFamilyStatement =
        "SELECT token_hash, family_id, device_id, player_id, issued_at_utc, expires_at_utc, rotated_at_utc " +
        "FROM auth_refresh_tokens WHERE family_id = @family;";

    /// <summary>Closes every live family an account holds, in one statement.</summary>
    public const string RevokeFamiliesForPlayerStatement =
        "UPDATE auth_token_families SET revoked_at_utc = @revoked, revoked_reason = @reason " +
        "WHERE player_id = @player AND revoked_at_utc IS NULL;";

    private readonly NpgsqlDataSource _dataSource;

    /// <summary>Opens the store over one pooled data source.</summary>
    /// <param name="dataSource">The adapter's data source.</param>
    public PostgresAuthStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>
    /// Opens the store over its own pooled data source. The composition root never sees an Npgsql
    /// type, so it hands over a connection string exactly as it does for the rest of this adapter.
    /// </summary>
    /// <param name="connectionString">The Postgres connection string.</param>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is null or blank.</exception>
    public static PostgresAuthStore Create(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException(
                "A blank connection string is a deployment that forgot ConnectionStrings:Postgres — " +
                "fail here, where the missing variable is nameable.",
                nameof(connectionString));
        }

        return new PostgresAuthStore(NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>The data source every statement above runs on.</summary>
    internal NpgsqlDataSource DataSource => _dataSource;

    /// <summary>Writes the account's first device row.</summary>
    /// <param name="device">The row to write. Carries a digest, never a secret.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> is null.</exception>
    public async Task InsertDeviceAsync(AuthDeviceRow device, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(device);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var insert = new NpgsqlCommand(InsertDeviceStatement, connection);

        BindDevice(insert, device);

        await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The device row behind one device id, or <c>null</c>.</summary>
    /// <param name="deviceId">The device id as presented.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<AuthDeviceRow?> FindDeviceAsync(string deviceId, CancellationToken ct) =>
        ReadDeviceAsync(SelectDeviceStatement, "device", deviceId, ct);

    /// <summary>The device row behind one account, or <c>null</c>.</summary>
    /// <param name="playerId">The account.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<AuthDeviceRow?> FindDeviceForPlayerAsync(string playerId, CancellationToken ct) =>
        ReadDeviceAsync(SelectDeviceForPlayerStatement, "player", playerId, ct);

    /// <summary>Whether this account carries a deletion record.</summary>
    /// <param name="playerId">The account.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task<bool> IsAccountDeletedAsync(string playerId, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var read = new NpgsqlCommand(SelectAccountDeletionStatement, connection);
        read.Parameters.AddWithValue("player", playerId);

        return await read.ExecuteScalarAsync(ct).ConfigureAwait(false) is string;
    }

    /// <summary>Every soft-deleted account, for the composition root's cached status view.</summary>
    /// <param name="ct">Cancellation.</param>
    public async Task<IReadOnlyList<string>> DeletedPlayersAsync(CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var read = new NpgsqlCommand(SelectDeletedPlayersStatement, connection);
        await using var reader = await read.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var players = new List<string>();

        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            players.Add(reader.GetString(0));
        }

        return players;
    }

    /// <summary>Opens a family with its first token and restamps the device's last-seen instant.</summary>
    /// <param name="family">The family being opened.</param>
    /// <param name="token">Its first refresh token, as a digest.</param>
    /// <param name="lastSeenAtUtc">The authentication instant the device row is restamped to.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException">A row is null.</exception>
    public async Task OpenTokenFamilyAsync(
        AuthTokenFamilyRow family,
        AuthRefreshTokenRow token,
        DateTimeOffset lastSeenAtUtc,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(family);
        ArgumentNullException.ThrowIfNull(token);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        // One transaction: a family with no token refuses the client's very first renewal, and a
        // token with no family is a credential nothing can ever revoke.
        await using (var insertFamily = new NpgsqlCommand(InsertFamilyStatement, connection, transaction))
        {
            BindFamily(insertFamily, family);
            await insertFamily.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var insertToken = new NpgsqlCommand(InsertRefreshTokenStatement, connection, transaction))
        {
            BindToken(insertToken, token);
            await insertToken.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var touch = new NpgsqlCommand(TouchDeviceStatement, connection, transaction))
        {
            touch.Parameters.AddWithValue("seen", Utc(lastSeenAtUtc));
            touch.Parameters.AddWithValue("device", family.DeviceId);
            await touch.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The family carrying one token digest, with every token in it.</summary>
    /// <param name="tokenHash">SHA-256 of the presented token.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="tokenHash"/> is null.</exception>
    public async Task<AuthTokenStateRows> LoadTokenStateForDigestAsync(byte[] tokenHash, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tokenHash);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        AuthTokenFamilyRow? family;

        await using (var readFamily = new NpgsqlCommand(SelectFamilyForTokenStatement, connection))
        {
            readFamily.Parameters.AddWithValue("hash", tokenHash);

            await using var reader = await readFamily.ExecuteReaderAsync(ct).ConfigureAwait(false);

            family = await reader.ReadAsync(ct).ConfigureAwait(false)
                ? new AuthTokenFamilyRow(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetFieldValue<DateTimeOffset>(3),
                    await NullableInstantAsync(reader, 4, ct).ConfigureAwait(false),
                    await reader.IsDBNullAsync(5, ct).ConfigureAwait(false) ? null : reader.GetString(5))
                : null;
        }

        if (family is null)
        {
            return AuthTokenStateRows.Empty;
        }

        // The whole family, not only the presented token: a reuse revokes every sibling, and the
        // rule that decides so is a pure function over exactly these rows.
        await using var readTokens = new NpgsqlCommand(SelectTokensInFamilyStatement, connection);
        readTokens.Parameters.AddWithValue("family", family.FamilyId);

        await using var tokens = await readTokens.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<AuthRefreshTokenRow>();

        while (await tokens.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new AuthRefreshTokenRow(
                tokens.GetFieldValue<byte[]>(0),
                tokens.GetString(1),
                tokens.GetString(2),
                tokens.GetString(3),
                tokens.GetFieldValue<DateTimeOffset>(4),
                tokens.GetFieldValue<DateTimeOffset>(5),
                await NullableInstantAsync(tokens, 6, ct).ConfigureAwait(false)));
        }

        return new AuthTokenStateRows(new[] { family }, rows);
    }

    /// <summary>Applies one rotation decision: the rotation stamp, the replacement, the revocations.</summary>
    /// <param name="write">What the decision resolved to, in rows.</param>
    /// <param name="nowUtc">The instant every stamp carries.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="write"/> is null.</exception>
    public async Task ApplyRotationAsync(AuthRotationWrite write, DateTimeOffset nowUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(write);

        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        if (write.RotatedTokenHash is { } rotated)
        {
            await StampRotatedAsync(connection, transaction, rotated, nowUtc, ct).ConfigureAwait(false);
        }

        if (write.IssuedToken is { } issued)
        {
            await using var insert = new NpgsqlCommand(InsertRefreshTokenStatement, connection, transaction);
            BindToken(insert, issued);
            await insert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        // The live siblings a revocation covers are stamped spent as well as the family being
        // closed: one row answers "is this family trustworthy", the other "is this token still one".
        foreach (var digest in write.RevokedTokenHashes)
        {
            await StampRotatedAsync(connection, transaction, digest, nowUtc, ct).ConfigureAwait(false);
        }

        if (write.RevokedReason is { } reason && write.FamilyId is { } familyId)
        {
            await using var revoke = new NpgsqlCommand(RevokeFamilyStatement, connection, transaction);
            revoke.Parameters.AddWithValue("revoked", Utc(nowUtc));
            revoke.Parameters.AddWithValue("reason", reason);
            revoke.Parameters.AddWithValue("family", familyId);
            await revoke.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Records the soft delete and closes every family the account holds.</summary>
    /// <param name="playerId">The account.</param>
    /// <param name="requestedAtUtc">When the player asked.</param>
    /// <param name="hardDeleteDueAtUtc">The date the hosted erasure sweep works to.</param>
    /// <param name="revokedReason">Why the families are closing.</param>
    /// <param name="ct">Cancellation.</param>
    public async Task SoftDeleteAccountAsync(
        string playerId,
        DateTimeOffset requestedAtUtc,
        DateTimeOffset hardDeleteDueAtUtc,
        string revokedReason,
        CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var record = new NpgsqlCommand(InsertAccountDeletionStatement, connection, transaction))
        {
            record.Parameters.AddWithValue("player", playerId);
            record.Parameters.AddWithValue("requested", Utc(requestedAtUtc));
            record.Parameters.AddWithValue("due", Utc(hardDeleteDueAtUtc));
            await record.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var revoke = new NpgsqlCommand(RevokeFamiliesForPlayerStatement, connection, transaction))
        {
            revoke.Parameters.AddWithValue("revoked", Utc(requestedAtUtc));
            revoke.Parameters.AddWithValue("reason", revokedReason);
            revoke.Parameters.AddWithValue("player", playerId);
            await revoke.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    private async Task<AuthDeviceRow?> ReadDeviceAsync(
        string statement, string parameterName, string value, CancellationToken ct)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var read = new NpgsqlCommand(statement, connection);
        read.Parameters.AddWithValue(parameterName, value);

        await using var reader = await read.ExecuteReaderAsync(ct).ConfigureAwait(false);

        return await reader.ReadAsync(ct).ConfigureAwait(false)
            ? new AuthDeviceRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetFieldValue<byte[]>(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.GetFieldValue<DateTimeOffset>(4))
            : null;
    }

    private static async Task StampRotatedAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        byte[] tokenHash,
        DateTimeOffset nowUtc,
        CancellationToken ct)
    {
        await using var stamp = new NpgsqlCommand(RotateRefreshTokenStatement, connection, transaction);
        stamp.Parameters.AddWithValue("rotated", Utc(nowUtc));
        stamp.Parameters.AddWithValue("hash", tokenHash);

        await stamp.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void BindDevice(NpgsqlCommand command, AuthDeviceRow device)
    {
        command.Parameters.AddWithValue("device", device.DeviceId);
        command.Parameters.AddWithValue("player", device.PlayerId);
        command.Parameters.AddWithValue("secret", device.SecretHash);
        command.Parameters.AddWithValue("created", Utc(device.CreatedAtUtc));
        command.Parameters.AddWithValue("seen", Utc(device.LastSeenAtUtc));
    }

    private static void BindFamily(NpgsqlCommand command, AuthTokenFamilyRow family)
    {
        command.Parameters.AddWithValue("family", family.FamilyId);
        command.Parameters.AddWithValue("device", family.DeviceId);
        command.Parameters.AddWithValue("player", family.PlayerId);
        command.Parameters.AddWithValue("created", Utc(family.CreatedAtUtc));
        command.Parameters.AddWithValue(
            "revoked", family.RevokedAtUtc is { } revoked ? Utc(revoked) : DBNull.Value);
        command.Parameters.AddWithValue("reason", (object?)family.RevokedReason ?? DBNull.Value);
    }

    private static void BindToken(NpgsqlCommand command, AuthRefreshTokenRow token)
    {
        command.Parameters.AddWithValue("hash", token.TokenHash);
        command.Parameters.AddWithValue("family", token.FamilyId);
        command.Parameters.AddWithValue("device", token.DeviceId);
        command.Parameters.AddWithValue("player", token.PlayerId);
        command.Parameters.AddWithValue("issued", Utc(token.IssuedAtUtc));
        command.Parameters.AddWithValue("expires", Utc(token.ExpiresAtUtc));
        command.Parameters.AddWithValue(
            "rotated", token.RotatedAtUtc is { } rotated ? Utc(rotated) : DBNull.Value);
    }

    private static async Task<DateTimeOffset?> NullableInstantAsync(
        NpgsqlDataReader reader, int ordinal, CancellationToken ct) =>
        await reader.IsDBNullAsync(ordinal, ct).ConfigureAwait(false)
            ? null
            : reader.GetFieldValue<DateTimeOffset>(ordinal);

    /// <summary>Every instant reaches a <c>timestamptz</c> at offset zero; Npgsql accepts no other.</summary>
    private static object Utc(DateTimeOffset instant) => instant.ToUniversalTime();
}

/// <summary>One <c>auth_devices</c> row. A digest, never a secret.</summary>
/// <param name="DeviceId">The server-minted device id.</param>
/// <param name="PlayerId">The account this device is the credential for.</param>
/// <param name="SecretHash">SHA-256 of the device secret.</param>
/// <param name="CreatedAtUtc">When the device was issued.</param>
/// <param name="LastSeenAtUtc">The authoritative last-seen instant.</param>
public sealed record AuthDeviceRow(
    string DeviceId,
    string PlayerId,
    byte[] SecretHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset LastSeenAtUtc);

/// <summary>One <c>auth_token_families</c> row.</summary>
/// <param name="FamilyId">The family's id.</param>
/// <param name="DeviceId">The device the family belongs to.</param>
/// <param name="PlayerId">The account.</param>
/// <param name="CreatedAtUtc">The horizon rotation may not extend past.</param>
/// <param name="RevokedAtUtc"><c>null</c> means live.</param>
/// <param name="RevokedReason">Set with <paramref name="RevokedAtUtc"/>.</param>
public sealed record AuthTokenFamilyRow(
    string FamilyId,
    string DeviceId,
    string PlayerId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? RevokedAtUtc,
    string? RevokedReason);

/// <summary>One <c>auth_refresh_tokens</c> row. A digest, never a token.</summary>
/// <param name="TokenHash">SHA-256 of the opaque token.</param>
/// <param name="FamilyId">The family it belongs to.</param>
/// <param name="DeviceId">The device.</param>
/// <param name="PlayerId">The account.</param>
/// <param name="IssuedAtUtc">When it was issued.</param>
/// <param name="ExpiresAtUtc">Never past its family's horizon.</param>
/// <param name="RotatedAtUtc"><c>null</c> means live.</param>
public sealed record AuthRefreshTokenRow(
    byte[] TokenHash,
    string FamilyId,
    string DeviceId,
    string PlayerId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? RotatedAtUtc);

/// <summary>The family and token rows one refresh presentation is decided over.</summary>
/// <param name="Families">The family the digest belongs to, or none.</param>
/// <param name="Tokens">Every token in that family.</param>
public sealed record AuthTokenStateRows(
    IReadOnlyList<AuthTokenFamilyRow> Families,
    IReadOnlyList<AuthRefreshTokenRow> Tokens)
{
    /// <summary>Nothing matched — what a lookup for an unknown digest returns.</summary>
    public static AuthTokenStateRows Empty { get; } =
        new(Array.Empty<AuthTokenFamilyRow>(), Array.Empty<AuthRefreshTokenRow>());
}

/// <summary>What one rotation decision writes. Digests only.</summary>
/// <param name="RotatedTokenHash">The presented digest, to be stamped spent.</param>
/// <param name="IssuedToken">The replacement row, on an accepted rotation.</param>
/// <param name="RevokedTokenHashes">Every digest a family revocation covers.</param>
/// <param name="FamilyId">The family being closed, when one is.</param>
/// <param name="RevokedReason">Why it is closing.</param>
public sealed record AuthRotationWrite(
    byte[]? RotatedTokenHash,
    AuthRefreshTokenRow? IssuedToken,
    IReadOnlyList<byte[]> RevokedTokenHashes,
    string? FamilyId,
    string? RevokedReason);
