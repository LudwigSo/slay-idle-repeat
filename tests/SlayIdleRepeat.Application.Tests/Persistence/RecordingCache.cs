using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Application.Tests.Persistence;

/// <summary>
/// A cache that records the order of the keys written through it, and can be told to refuse one.
/// </summary>
/// <remarks>
/// A decorator rather than a second fake: the store's behaviour must be observed against the shipped
/// in-memory cache, and only the write order and the injected failure are this class's own.
/// </remarks>
internal sealed class RecordingCache : ILocalCachePort
{
    private readonly ILocalCachePort _inner;
    private readonly List<string> _writes = [];
    private string? _refuses;
    private bool _refusesEverything;

    /// <summary>Wraps a cache.</summary>
    /// <param name="inner">The cache that actually stores the bytes.</param>
    internal RecordingCache(ILocalCachePort inner) => _inner = inner;

    /// <summary>Every key written through this cache, in write order, including a refused write.</summary>
    internal IReadOnlyList<string> Writes => _writes;

    /// <summary>The failure a refused write throws, so a case can name what it expects to escape.</summary>
    internal const string RefusalMessage = "the backing store refused this write";

    /// <summary>Makes every write of <paramref name="key"/> throw.</summary>
    /// <param name="key">The key to refuse.</param>
    /// <returns>This cache, so a fixture reads as one expression.</returns>
    internal RecordingCache Refusing(string key)
    {
        _refuses = key;

        return this;
    }

    /// <summary>Makes every write throw, whatever its key — the shape a read path must survive.</summary>
    /// <returns>This cache, so a fixture reads as one expression.</returns>
    internal RecordingCache RefusingEveryWrite()
    {
        _refusesEverything = true;

        return this;
    }

    /// <inheritdoc/>
    public Task<byte[]?> ReadAsync(string key, CancellationToken ct) => _inner.ReadAsync(key, ct);

    /// <inheritdoc/>
    public Task WriteAsync(string key, ReadOnlyMemory<byte> value, CancellationToken ct)
    {
        _writes.Add(key);

        return _refusesEverything || string.Equals(key, _refuses, StringComparison.Ordinal)
            ? throw new IOException(RefusalMessage + ": '" + key + "'")
            : _inner.WriteAsync(key, value, ct);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string key, CancellationToken ct) => _inner.DeleteAsync(key, ct);
}
