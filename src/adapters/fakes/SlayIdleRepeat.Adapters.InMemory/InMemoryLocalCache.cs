using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The in-memory fake for <see cref="ILocalCachePort"/>, backed by a dictionary a scenario can
/// hand to more than one port.
/// </summary>
/// <remarks>
/// <para>
/// The backing dictionary, not the port object, is the store — which is what makes
/// <see cref="Reopen"/> mean what the next launch of the app means. A fake keeping its entries in
/// a field of the port would satisfy every round-trip case and lose everything at the moment the
/// real adapter keeps it.
/// </para>
/// <para>
/// 🔒 <b>Copies on the way in as well as on the way out.</b> The port promises only the read-side
/// copy; this is an implementation guarantee on top of it, and it exists because the file-backed
/// adapter has it for free. Alias the caller's buffer here and a scenario that reuses one array
/// across two writes silently rewrites what it already stored — against this implementation only,
/// which is the drift a shared contract suite cannot see and this class must not introduce.
/// </para>
/// </remarks>
public sealed class InMemoryLocalCache : ILocalCachePort
{
    private const string KeySpace = "A-Z, a-z, 0-9, '.', '_' and '-'";

    private readonly Dictionary<string, byte[]> _entries;

    /// <summary>Creates a cache over a backing store of its own.</summary>
    public InMemoryLocalCache()
        : this(new Dictionary<string, byte[]>(StringComparer.Ordinal))
    {
    }

    private InMemoryLocalCache(Dictionary<string, byte[]> entries) => _entries = entries;

    /// <summary>
    /// A fresh port over the same backing store — what the next launch of the app opens.
    /// </summary>
    public InMemoryLocalCache Reopen() => new(_entries);

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/> without a scenario having to
    /// await a write, through the same key space and the same copy as <see cref="WriteAsync"/>.
    /// </summary>
    /// <param name="key">A key inside the port's key space.</param>
    /// <param name="value">The bytes to store.</param>
    /// <returns>This cache, so several entries chain.</returns>
    /// <exception cref="ArgumentException"><paramref name="key"/> is outside the key space.</exception>
    public InMemoryLocalCache Seed(string key, ReadOnlyMemory<byte> value)
    {
        _entries[Checked(key)] = value.ToArray();
        return this;
    }

    /// <inheritdoc/>
    public Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        var entry = Checked(key);
        ct.ThrowIfCancellationRequested();

        return Task.FromResult(_entries.TryGetValue(entry, out var value) ? value.ToArray() : null);
    }

    /// <inheritdoc/>
    public Task WriteAsync(string key, ReadOnlyMemory<byte> value, CancellationToken ct)
    {
        var entry = Checked(key);
        ct.ThrowIfCancellationRequested();

        _entries[entry] = value.ToArray();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var entry = Checked(key);
        ct.ThrowIfCancellationRequested();

        _entries.Remove(entry);
        return Task.CompletedTask;
    }

    private static string Checked(string key) =>
        string.IsNullOrEmpty(key) || !key.All(IsKeyCharacter)
            ? throw new ArgumentException(
                $"'{key}' is outside this cache's key space, which is non-empty and made only of " +
                $"{KeySpace}. The key space is the port's, not this implementation's: a key the fake " +
                "accepts and the file-backed adapter rejects is a scenario that only passes here.",
                nameof(key))
            : key;

    private static bool IsKeyCharacter(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                     or '.' or '_' or '-';
}
