using System.Text;
using SlayIdleRepeat.Application.Ports.Client;

namespace SlayIdleRepeat.Adapters.Cache.LocalFile;

/// <summary>
/// The real <see cref="ILocalCachePort"/>: one file per key under a directory the host chooses.
/// </summary>
/// <remarks>
/// <para>
/// The whole implementation is the port's key space plus <c>System.IO</c>. A key is non-empty and
/// made only of <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c> and <c>-</c>, so no key can
/// carry a separator, a <c>..</c> segment or a drive letter, and the only path arithmetic below is
/// a single <see cref="Path.Combine(string, string)"/>.
/// </para>
/// <para>
/// 🔒 <b>The file name is the key hex-encoded, not the key.</b> That is not a sanitiser — a
/// sanitiser maps two keys onto one name, and this mapping is injective, so the closed key space
/// still carries the whole safety argument. It is there because a name taken verbatim is compared
/// by the filesystem rather than ordinally, and three separate host behaviours then merge keys the
/// port keeps apart: Windows and the default macOS filesystem ignore case, so <c>Profile</c> and
/// <c>profile</c> become one entry; Win32 strips a trailing dot, so <c>a.</c> and <c>a</c> become
/// one entry; and <c>con</c>, <c>nul</c> and their siblings are device names on Windows, where a
/// write goes to the console or is discarded outright. Hex output is lower case, dot-free and
/// spelled from <c>0-9a-f</c>, which none of those three can reach.
/// </para>
/// <para>
/// A miss is an absent file and a cached empty value is a zero-length one, which is the same
/// distinction the port draws and costs nothing to keep here.
/// </para>
/// <para>
/// 🔒 <b>A write lands whole or not at all.</b> The bytes go to a scratch file beside the entry and
/// are moved over it once they are all there, so a cancelled or failed write leaves the previous
/// value rather than a truncated one. Truncation is the failure worth the extra move: a short file
/// reads back as a <em>hit</em> carrying bytes the caller never stored, and no reader can tell it
/// from a short value that was stored on purpose.
/// </para>
/// </remarks>
public sealed class LocalFileCache : ILocalCachePort
{
    private const string KeySpace = "A-Z, a-z, 0-9, '.', '_' and '-'";

    /// <summary>Marks a half-written entry. Hex names hold no dot, so no entry can be mistaken for one.</summary>
    private const string ScratchSuffix = ".partial";

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

        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            // The entry went away between the probe and the open. Nothing is stored under the key,
            // which is a miss — and letting the race surface as an exception would make a caller
            // handle a fault for the one answer this port already has a value for.
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task WriteAsync(string key, ReadOnlyMemory<byte> value, CancellationToken ct)
    {
        var path = PathOf(key);
        ct.ThrowIfCancellationRequested();

        var scratch = path + "." + Path.GetRandomFileName() + ScratchSuffix;

        try
        {
            await using (var stream = new FileStream(
                             scratch,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             useAsync: true))
            {
                await stream.WriteAsync(value, ct).ConfigureAwait(false);
            }

            File.Move(scratch, path, overwrite: true);
        }
        catch
        {
            Discard(scratch);
            throw;
        }
    }

    /// <inheritdoc/>
    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathOf(key);
        ct.ThrowIfCancellationRequested();

        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // The store's whole directory is gone, which the port allows at any moment. Nothing is
            // stored under the key, which is exactly the state a delete is asked to arrange.
        }

        return Task.CompletedTask;
    }

    private string PathOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (!IsInKeySpace(key))
        {
            throw new ArgumentException(
                $"'{key}' is outside this cache's key space, which is non-empty and made only of " +
                $"{KeySpace}. A key names an entry inside the store and can never name a location " +
                "outside it.",
                nameof(key));
        }

        return Path.Combine(CacheDirectoryPath, FileNameOf(key));
    }

    private static void Discard(string scratch)
    {
        try
        {
            File.Delete(scratch);
        }
        catch (Exception cleanupFailure) when (cleanupFailure is IOException or UnauthorizedAccessException)
        {
            // Best effort. A scratch file is litter no read ever looks at, and replacing the
            // failure the caller is being told about with a cleanup fault would hide the real one.
        }
    }

    private static string FileNameOf(string key) =>
        Convert.ToHexString(Encoding.UTF8.GetBytes(key)).ToLowerInvariant();

    private static bool IsInKeySpace(string key)
    {
        if (key.Length == 0)
        {
            return false;
        }

        foreach (var character in key)
        {
            if (!IsKeyCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsKeyCharacter(char character) =>
        character is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9')
                     or '.' or '_' or '-';
}
