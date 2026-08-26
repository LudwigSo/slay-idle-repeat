namespace SlayIdleRepeat.Adapters.Cache.Redis;

/// <summary>The one class that speaks the vendor protocol: <see cref="IVolatileByteCache"/> over a live connection.</summary>
public sealed class RedisVolatileByteCache : IVolatileByteCache, IDisposable
{
    private RedisVolatileByteCache() =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <summary>Connects to the configured endpoint. The composition root hands this a configuration string and never sees a vendor type.</summary>
    /// <param name="configuration">The connection configuration string.</param>
    public static RedisVolatileByteCache Connect(string configuration) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <inheritdoc/>
    public Task<byte[]?> GetAsync(string key, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <inheritdoc/>
    public Task SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan ttl, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <inheritdoc/>
    public Task DeleteAsync(string key, CancellationToken ct) =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");

    /// <inheritdoc/>
    public void Dispose() =>
        throw new NotImplementedException("M5-05 phase 3 implements the Redis adapter.");
}
