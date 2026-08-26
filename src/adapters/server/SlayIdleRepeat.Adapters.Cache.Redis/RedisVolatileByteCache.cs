using StackExchange.Redis;

namespace SlayIdleRepeat.Adapters.Cache.Redis;

/// <summary>The one class that speaks the vendor protocol: <see cref="IVolatileByteCache"/> over a live connection.</summary>
/// <remarks>
/// A thin translation and nothing else — the decorators own every policy. Vendor faults leave this
/// class as they are; the decorators absorb and count them, which is what keeps a dead endpoint a
/// latency story rather than an outage.
/// </remarks>
public sealed class RedisVolatileByteCache : IVolatileByteCache, IDisposable
{
    private readonly ConnectionMultiplexer _connection;

    private RedisVolatileByteCache(ConnectionMultiplexer connection) => _connection = connection;

    /// <summary>Connects to the configured endpoint. The composition root hands this a configuration string and never sees a vendor type.</summary>
    /// <param name="configuration">The connection configuration string.</param>
    /// <exception cref="ArgumentException"><paramref name="configuration"/> is null or blank.</exception>
    public static RedisVolatileByteCache Connect(string configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration))
        {
            throw new ArgumentException(
                "A blank configuration is a deployment that forgot ConnectionStrings:Redis — fail " +
                "here, where the missing variable is nameable.",
                nameof(configuration));
        }

        return new RedisVolatileByteCache(ConnectionMultiplexer.Connect(configuration));
    }

    /// <inheritdoc/>
    public async Task<byte[]?> GetAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var value = await _connection.GetDatabase().StringGetAsync(key).ConfigureAwait(false);

        return value.IsNull ? null : (byte[]?)value;
    }

    /// <inheritdoc/>
    public async Task SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan ttl, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await _connection.GetDatabase().StringSetAsync(key, value.ToArray(), ttl).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task DeleteAsync(string key, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await _connection.GetDatabase().KeyDeleteAsync(key).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose() => _connection.Dispose();
}
