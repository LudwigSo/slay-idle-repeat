namespace SlayIdleRepeat.Application.Ports.Client;

/// <summary>
/// The client's on-device byte cache: a keyed store of blobs the client filled from state the
/// server issued it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>What this port is not.</b> It is a cache, and a caller may discard the whole of it at any
/// moment — on an eviction, a reinstall, a schema change, or because a read came back malformed.
/// A hit is <b>never authoritative</b>. Nothing may decide a game outcome from one or reconcile a
/// server answer against one: where there is a server, its answer wins unconditionally and a cached
/// value that disagrees is simply stale.
/// </para>
/// <para>
/// ⚠️ <b>Two clients use this port and only one of them has a server, so the cost of an empty store
/// differs — it is stated per client rather than as one absolute only one of them obeys.</b> For
/// the server-authoritative client this store holds the read-only mirror of the last profile and
/// run state the server issued: emptying it costs a cold start its instant display and nothing
/// else, because the next answer refills it, so anything kept there that would break on an empty
/// store is in the wrong place. For the in-process client — which is what a shipped build still
/// composes — this store is the only place the local profile is written, and emptying it starts a
/// new local game. That is not a second source of truth: there is no other source for it to be
/// second to, and no server answer is ever reconciled against it. It does mean the sentence above
/// about an empty store is a claim about the mirror and not about the local profile.
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
