using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
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

        var canonical = ContentHashing.CanonicalBytes(documents);

        using var compressed = new MemoryStream();

        // Disposed before ToArray: GZipStream writes its footer on dispose, and a bundle read back
        // without one is a truncation the reader would correctly refuse.
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(canonical, 0, canonical.Length);
        }

        return compressed.ToArray();
    }

    /// <summary>Decodes a bundle back into the documents it was packed from.</summary>
    /// <param name="bundle">The compressed bundle.</param>
    /// <returns>The documents, in ordinal path order.</returns>
    /// <exception cref="ContentBundleFormatException">
    /// The stream is not readable gzip, is truncated, leads with an unknown
    /// <see cref="ContentHashing.CanonicalFormatVersion"/>, or carries an unknown value tag.
    /// </exception>
    public static IReadOnlyList<ContentDocument> Unpack(ReadOnlyMemory<byte> bundle)
    {
        var reader = new CanonicalReader(Decompress(bundle));

        var format = reader.ReadByte("the canonical format version");
        if (format != ContentHashing.CanonicalFormatVersion)
        {
            throw new ContentBundleFormatException(string.Create(
                CultureInfo.InvariantCulture,
                $"This bundle leads with canonical format version {format}, and this build reads " +
                $"version {ContentHashing.CanonicalFormatVersion}. The encoding moved, so the bytes " +
                $"mean something this reader would guess at rather than know."));
        }

        var count = reader.ReadCount("the document count");
        var documents = new List<ContentDocument>(count);

        for (var index = 0; index < count; index++)
        {
            var path = reader.ReadString(
                string.Create(CultureInfo.InvariantCulture, $"document {index}'s path"));
            documents.Add(new ContentDocument(path, reader.ReadValue()));
        }

        reader.DemandEnd(documents.Count);

        return documents;
    }

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

        var documents = Unpack(bundle);
        var recomputed = ContentHashing.Compute(documents);

        if (!recomputed.Equals(expected))
        {
            throw new ContentBundleFormatException(
                "This bundle re-stamps to " + recomputed.Value + ", and it was served as " +
                expected.Value + ". The bytes are not the content set that stamp names, so nothing " +
                "in them can be believed.");
        }

        return new ContentSnapshot(recomputed, documents);
    }

    private static byte[] Decompress(ReadOnlyMemory<byte> bundle)
    {
        try
        {
            using var source = new MemoryStream(bundle.ToArray(), writable: false);
            using var gzip = new GZipStream(source, CompressionMode.Decompress);
            using var canonical = new MemoryStream();

            gzip.CopyTo(canonical);

            return canonical.ToArray();
        }
        catch (InvalidDataException fault)
        {
            throw new ContentBundleFormatException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"These {bundle.Length} bytes are not readable gzip, so no content was ever " +
                    $"reached. That is a transport or storage fault, not a content one."),
                fault);
        }
    }

    /// <summary>Reads the canonical encoding back, refusing anything it cannot account for.</summary>
    /// <remarks>
    /// A struct over a span-free byte array rather than a <c>Stream</c>: every read is
    /// length-checked against the same cursor, so "the stream ran out" is one code path that can
    /// name the offset it ran out at instead of a <c>ReadByte</c> returning <c>-1</c> somewhere
    /// deep in a recursion.
    /// </remarks>
    private sealed class CanonicalReader(byte[] canonical)
    {
        private int _offset;

        internal byte ReadByte(string what)
        {
            Demand(1, what);

            return canonical[_offset++];
        }

        internal int ReadCount(string what)
        {
            Demand(4, what);

            var count = BinaryPrimitives.ReadInt32LittleEndian(canonical.AsSpan(_offset, 4));
            _offset += 4;

            if (count < 0)
            {
                throw new ContentBundleFormatException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The bundle declares a length of {count} for {what}. A negative count cannot " +
                    $"have been written by the canonical encoder, so these bytes were not produced " +
                    $"by one."));
            }

            return count;
        }

        internal string ReadString(string what)
        {
            var length = ReadCount("the length of " + what);
            Demand(length, what);

            var text = Encoding.UTF8.GetString(canonical, _offset, length);
            _offset += length;

            return text;
        }

        internal ContentValue ReadValue()
        {
            var tag = ReadByte("a value tag");

            switch (tag)
            {
                case ContentHashing.TagUnauthorised:
                    return ContentValue.Unauthorised;

                case ContentHashing.TagFalse:
                    return ContentValue.False;

                case ContentHashing.TagTrue:
                    return ContentValue.True;

                case ContentHashing.TagNumber:
                    return ContentValue.Number(ReadNumber());

                case ContentHashing.TagText:
                    return ContentValue.Text(ReadString("a text value"));

                case ContentHashing.TagArray:
                    return ReadArray();

                case ContentHashing.TagObject:
                    return ReadObject();

                default:
                    throw new ContentBundleFormatException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"The bundle carries value tag 0x{tag:x2} ({tag}) at offset {_offset - 1}, " +
                        $"which the canonical encoding has no meaning for. Either the bytes are " +
                        $"damaged or they were written by an encoder this build does not know."));
            }
        }

        internal void DemandEnd(int documents)
        {
            if (_offset != canonical.Length)
            {
                throw new ContentBundleFormatException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The bundle's {documents} documents ended at offset {_offset} of " +
                    $"{canonical.Length} bytes, leaving {canonical.Length - _offset} unaccounted " +
                    $"for. Trailing bytes are content nobody would hash."));
            }
        }

        private decimal ReadNumber()
        {
            var written = ReadString("a number value");

            return decimal.TryParse(written, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value
                : throw new ContentBundleFormatException(
                    $"The bundle writes '{written}' where the canonical encoding puts a normalised " +
                    "invariant decimal. That is not a number this reader can round-trip.");
        }

        private ContentValue ReadArray()
        {
            var count = ReadCount("an array length");
            var items = new List<ContentValue>(Math.Min(count, 64));

            for (var index = 0; index < count; index++)
            {
                items.Add(ReadValue());
            }

            return ContentValue.Array(items);
        }

        private ContentValue ReadObject()
        {
            var count = ReadCount("an object's member count");
            var members = new List<KeyValuePair<string, ContentValue>>(Math.Min(count, 64));

            for (var index = 0; index < count; index++)
            {
                var name = ReadString("an object member name");
                members.Add(new KeyValuePair<string, ContentValue>(name, ReadValue()));
            }

            return ContentValue.Object(members);
        }

        private void Demand(int bytes, string what)
        {
            if (_offset + bytes > canonical.Length)
            {
                throw new ContentBundleFormatException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The bundle is truncated: {what} wants {bytes} bytes at offset {_offset}, and " +
                    $"the canonical payload is only {canonical.Length} bytes long."));
            }
        }
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
