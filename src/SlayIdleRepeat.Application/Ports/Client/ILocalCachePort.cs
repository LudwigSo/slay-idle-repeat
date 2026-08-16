namespace SlayIdleRepeat.Application.Ports.Client;

/// <summary>
/// The client's on-device byte cache: a keyed store of blobs the client filled from state the
/// server issued it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What this port is not.</b> It is a cache, and the client may discard the whole of it at any
/// moment — on an eviction, a reinstall, a schema change, or because a read came back malformed.
/// It is <b>never</b> a second source of truth. Nothing may treat a hit as authoritative, decide a
/// game outcome from one, or reconcile a server answer against one; the server's answer wins
/// unconditionally, and a cached value that disagrees is simply stale. Anything that would break if
/// this store were empty on the next launch is in the wrong place.
/// </para>
/// <para>
/// It is byte-oriented rather than generic on purpose. A <c>ReadAsync&lt;T&gt;</c> would put a
/// serializer inside the port, which means each implementation serializes on its own and the shared
/// contract suite can no longer pin what is actually stored. Serialization is deterministic
/// computation over bytes and belongs above this seam.
/// </para>
/// </remarks>
public interface ILocalCachePort
{
    /// <summary>
    /// The bytes stored under <paramref name="key"/>, or <see langword="null"/> when nothing is
    /// stored under it.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>A miss and a cached empty value are different answers and the difference is
    /// load-bearing.</b> A miss is <see langword="null"/>; a value that was written as zero bytes
    /// reads back as an empty array. Collapsing the two makes "we have never fetched this" and "the
    /// server told us this is empty" indistinguishable, and the client then re-fetches forever or
    /// caches an absence it never observed.
    /// <para>
    /// The returned array is a <b>copy</b>: mutating it does not change what is cached, and a
    /// subsequent read returns the original bytes.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="key"/> is outside the key space.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> is already cancelled.</exception>
    Task<byte[]?> ReadAsync(string key, CancellationToken ct);

    /// <summary>
    /// Stores <paramref name="value"/> under <paramref name="key"/>, last write wins. Writing zero
    /// bytes stores an empty value, which is a hit rather than a miss.
    /// </summary>
    /// <remarks>
    /// 🔒 <b>The key space is closed and ordinal</b>: a key is non-empty and consists only of
    /// <c>A-Z</c>, <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>_</c> and <c>-</c>. Anything else — a space,
    /// a colon, either path separator, a <c>..</c> segment — is an <see cref="ArgumentException"/>
    /// from every method on this port. That is what makes a file-backed implementation safe by
    /// construction rather than by a sanitiser each implementation writes for itself: there is no
    /// key that can name a location outside the store's own directory.
    /// <para>
    /// Values outlive the port instance. A fresh port opened over the same backing store reads back
    /// everything the previous one wrote.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="key"/> is outside the key space.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> is already cancelled.</exception>
    Task WriteAsync(string key, ReadOnlyMemory<byte> value, CancellationToken ct);

    /// <summary>
    /// Removes whatever is stored under <paramref name="key"/>, after which a read of that key is a
    /// miss. Deleting a key that is not present is a no-op and not a fault — a caller clearing a
    /// cache does not first have to establish what is in it.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="key"/> is outside the key space.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> is already cancelled.</exception>
    Task DeleteAsync(string key, CancellationToken ct);
}
