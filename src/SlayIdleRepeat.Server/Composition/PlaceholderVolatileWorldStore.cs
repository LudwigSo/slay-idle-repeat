using System.Collections.Concurrent;
using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Server.Composition;

/// <summary>
/// ⚠️ PLACEHOLDER — the byte store the server's <c>WorldSliceStore</c> runs over until M5-05 lands
/// the Postgres-authoritative row and the Redis hot cache. Process-lifetime memory: a restart
/// loses every profile and run, which is tolerable only while no real client depends on this
/// server. Deliberately in the composition root, not in <c>adapters/fakes</c>: the test fakes stay
/// test-only, and this concrete type is named exactly where concrete adapter types are allowed.
/// </summary>
/// <remarks>
/// Honours the port's contract where it is cheap and load-bearing: the closed ordinal key space
/// (what makes every backing safe by construction) and copy-out semantics (what keeps a caller's
/// mutation out of the store). The M5-05 backing inherits the same contract through the port's
/// shared suite.
/// </remarks>
public sealed class PlaceholderVolatileWorldStore : ILocalCachePort
{
    private readonly ConcurrentDictionary<string, byte[]> _rows = new(StringComparer.Ordinal);

    /// <inheritdoc/>
    public Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        RequireKey(key);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(_rows.TryGetValue(key, out var value) ? value.ToArray() : (byte[]?)null);
    }

    /// <inheritdoc/>
    public Task WriteAsync(string key, ReadOnlyMemory<byte> value, CancellationToken ct)
    {
        RequireKey(key);
        ct.ThrowIfCancellationRequested();

        _rows[key] = value.ToArray();

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string key, CancellationToken ct)
    {
        RequireKey(key);
        ct.ThrowIfCancellationRequested();

        _rows.TryRemove(key, out _);

        return Task.CompletedTask;
    }

    private static void RequireKey(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A cache key is non-empty (ILocalCachePort).", nameof(key));
        }

        foreach (var character in key)
        {
            if (character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '_' or '-')
            {
                continue;
            }

            throw new ArgumentException(
                $"'{key}' is outside ILocalCachePort's closed key space (A-Z a-z 0-9 . _ -). The " +
                "space is what makes every backing safe by construction, so this store enforces it " +
                "even though memory has no paths to escape.",
                nameof(key));
        }
    }
}
