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

    private readonly NpgsqlDataSource _dataSource;

    /// <summary>Opens the store over one pooled data source.</summary>
    /// <param name="dataSource">The adapter's data source.</param>
    public PostgresAuthStore(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    /// <summary>The data source every statement above runs on.</summary>
    internal NpgsqlDataSource DataSource => _dataSource;
}
