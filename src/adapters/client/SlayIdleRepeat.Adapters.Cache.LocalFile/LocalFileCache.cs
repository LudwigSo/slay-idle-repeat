using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Adapters.Cache.LocalFile;

/// <summary>
/// The real <see cref="ILocalCachePort"/>: one file per key under a directory the host chooses.
/// </summary>
/// <remarks>
/// <para>
/// The whole implementation is the port's key space plus <c>System.IO</c>. A key is non-empty and
/// made only of <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c> and <c>-</c>, so it is a
/// file name and cannot be anything else — no separator, no <c>..</c> segment, no drive letter.
/// That is what makes this store safe by construction rather than by a sanitiser, and it is why
/// there is no path arithmetic below beyond a single <see cref="Path.Combine(string, string)"/>.
/// </para>
/// <para>
/// A miss is an absent file and a cached empty value is a zero-length one, which is the same
/// distinction the port draws and costs nothing to keep here.
/// </para>
/// <para>
/// ⚠️ The file name <b>is</b> the key, so two keys differing only in case name one file on a
/// case-insensitive filesystem, where the port's key space is ordinal. No caller mints a key that
/// way today. If one ever does, encode the name here rather than narrowing the key space — the
/// closed key space is what the other implementation relies on too.
/// </para>
/// </remarks>
public sealed class LocalFileCache : ILocalCachePort
{
    private const string KeySpace = "A-Z, a-z, 0-9, '.', '_' and '-'";

    /// <summary>Creates a cache over <paramref name="cacheDirectoryPath"/>, creating it if absent.</summary>
    /// <param name="cacheDirectoryPath">The directory this cache owns.</param>
    public LocalFileCache(string cacheDirectoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectoryPath);

        CacheDirectoryPath = Path.GetFullPath(cacheDirectoryPath);
        Directory.CreateDirectory(CacheDirectoryPath);
    }

    /// <summary>The directory holding this cache's entries.</summary>
    public string CacheDirectoryPath { get; }

    /// <inheritdoc/>
    public async Task<byte[]?> ReadAsync(string key, CancellationToken ct)
    {
        var path = PathOf(key);
        ct.ThrowIfCancellationRequested();

        return File.Exists(path)
            ? await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false)
            : null;
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string key, ReadOnlyMemory<byte> value, CancellationToken ct)
    {
        var path = PathOf(key);
        ct.ThrowIfCancellationRequested();

        await File.WriteAllBytesAsync(path, value.ToArray(), ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathOf(key);
        ct.ThrowIfCancellationRequested();

        File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathOf(string key)
    {
        if (string.IsNullOrEmpty(key) || !key.All(IsKeyCharacter))
        {
            throw new ArgumentException(
                $"'{key}' is outside this cache's key space, which is non-empty and made only of " +
                $"{KeySpace}. A key names an entry inside the store and can never name a location " +
                "outside it.",
                nameof(key));
        }

        return Path.Combine(CacheDirectoryPath, key);
    }

    private static bool IsKeyCharacter(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                     or '.' or '_' or '-';
}
