using SlayIdleRepeat.Application.Ports.Server;

namespace SlayIdleRepeat.Adapters.ObjectStore.S3;

/// <summary>How the composition root configures the object-store backing, in primitives only.</summary>
/// <param name="ServiceUrl">The endpoint URL.</param>
/// <param name="Region">The region name.</param>
/// <param name="ForcePathStyle">Whether to address buckets by path — true for the local stack.</param>
/// <param name="AccessKey">The application account's access key.</param>
/// <param name="SecretKey">The application account's secret.</param>
/// <param name="Bucket">The bucket battle logs live in.</param>
public sealed record S3ObjectStoreOptions(
    string ServiceUrl,
    string Region,
    bool ForcePathStyle,
    string AccessKey,
    string SecretKey,
    string Bucket);

/// <summary>The S3-backed <see cref="IBattleLogStore"/>: durable put/get, gzip at rest, the id mapped to an object name internally.</summary>
public sealed class S3BattleLogStore : IBattleLogStore, IDisposable
{
    private S3BattleLogStore() =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");

    /// <summary>Opens the store. The composition root hands this primitives and never sees a vendor type.</summary>
    /// <param name="options">The backing's configuration.</param>
    public static S3BattleLogStore Create(S3ObjectStoreOptions options) =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");

    /// <summary>The object name a log is stored under — the whole of the id-to-storage mapping, decided once.</summary>
    /// <param name="id">The log's identity.</param>
    public static string StorageNameOf(BattleLogId id) =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");

    /// <inheritdoc/>
    public Task PutAsync(BattleLogId id, ReadOnlyMemory<byte> log, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");

    /// <inheritdoc/>
    public Task<ReadOnlyMemory<byte>?> GetAsync(BattleLogId id, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");

    /// <inheritdoc/>
    public void Dispose() =>
        throw new NotImplementedException("M5-05 phase 3 implements the object-store adapter.");
}
