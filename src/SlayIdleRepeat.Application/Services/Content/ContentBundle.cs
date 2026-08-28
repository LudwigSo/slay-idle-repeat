using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// The shippable form of a <see cref="ContentSnapshot"/>: gzip over
/// <see cref="ContentHashing.CanonicalBytes"/>, and back again.
/// </summary>
/// <remarks>
/// <para>
/// There is no second serialisation here. The bytes a bundle carries are exactly the bytes the
/// stamp is taken over, so packing and hashing can never disagree about what a version means —
/// a bundle format of its own would be a second encoding to keep in step with the first, and the
/// day the two drifted a client would verify a hash the server never computed.
/// </para>
/// <para>
/// 🔒 <see cref="Open"/> RECOMPUTES the stamp from the unpacked documents and refuses anything that
/// does not equal the expected one. A bundle is fetched over a network from a store nobody in this
/// process controls; trusting its file name — or a version written inside it — would make the
/// stamp a label rather than a proof.
/// </para>
/// </remarks>
public static class ContentBundle
{
    /// <summary>Packs documents into the wire form: gzip of the canonical bytes.</summary>
    /// <param name="documents">The snapshot's documents. Order does not matter; the canonical encoding sorts them.</param>
    /// <returns>The compressed bundle.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="documents"/> is null.</exception>
    public static byte[] Pack(IEnumerable<ContentDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        throw new NotImplementedException();
    }

    /// <summary>Decodes a bundle back into the documents it was packed from.</summary>
    /// <param name="bundle">The compressed bundle.</param>
    /// <returns>The documents, in ordinal path order.</returns>
    /// <exception cref="ContentBundleFormatException">
    /// The stream is not readable gzip, is truncated, leads with an unknown
    /// <see cref="ContentHashing.CanonicalFormatVersion"/>, or carries an unknown value tag.
    /// </exception>
    public static IReadOnlyList<ContentDocument> Unpack(ReadOnlyMemory<byte> bundle) =>
        throw new NotImplementedException();

    /// <summary>Unpacks a bundle, re-stamps it, and hands back a snapshot only if the stamp matches.</summary>
    /// <param name="bundle">The compressed bundle.</param>
    /// <param name="expected">The version the caller asked for.</param>
    /// <returns>The snapshot the bundle carries, stamped with the recomputed version.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expected"/> is null.</exception>
    /// <exception cref="ContentBundleFormatException">
    /// The bundle is unreadable, or the recomputed stamp is not <paramref name="expected"/>.
    /// </exception>
    public static ContentSnapshot Open(ReadOnlyMemory<byte> bundle, ContentVersion expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        throw new NotImplementedException();
    }
}

/// <summary>
/// Raised when a bundle cannot be read as one, or reads as a different version than the one asked
/// for.
/// </summary>
/// <remarks>
/// Every message names what was actually found — the format byte, the offset the stream ran out
/// at, the tag value, the recomputed stamp beside the expected one. "The bundle is corrupt" is a
/// sentence that has never once told anybody which of four things happened.
/// </remarks>
public sealed class ContentBundleFormatException : Exception
{
    /// <summary>Creates the exception with a message naming what the reader found.</summary>
    /// <param name="message">What was found, not merely that something was wrong.</param>
    public ContentBundleFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the decompression fault underneath it.</summary>
    /// <param name="message">What was found, not merely that something was wrong.</param>
    /// <param name="innerException">The underlying fault, carried rather than swallowed.</param>
    public ContentBundleFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
