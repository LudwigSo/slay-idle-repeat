namespace SlayIdleRepeat.Adapters.Cache.Redis;

/// <summary>This adapter's own seam over the volatile byte store: get, set-with-lifetime, delete.</summary>
/// <remarks>
/// Adapter-internal by design — NOT a port, and deliberately not under <c>Ports/</c>: it exists so
/// the two cache decorators' policy (write-behind, swallow-and-count, fall back and repopulate) is
/// unit-testable against a fake surface, while the one class that speaks the vendor protocol stays
/// a thin translation. Any throw from an implementation is a cache failure the decorators absorb.
/// </remarks>
public interface IVolatileByteCache
{
    /// <summary>The cached bytes, or <c>null</c> on a miss.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="ct">Cancellation.</param>
    Task<byte[]?> GetAsync(string key, CancellationToken ct);

    /// <summary>Stores bytes under a key with an absolute lifetime.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="value">The bytes.</param>
    /// <param name="ttl">The entry's lifetime. Positive.</param>
    /// <param name="ct">Cancellation.</param>
    Task SetAsync(string key, ReadOnlyMemory<byte> value, TimeSpan ttl, CancellationToken ct);

    /// <summary>Removes a key. Removing an absent key is a no-op.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="ct">Cancellation.</param>
    Task DeleteAsync(string key, CancellationToken ct);
}
