using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// Computes the <see cref="ContentVersion"/> stamp: SHA-256 over a canonical serialisation of the
/// loaded documents.
/// </summary>
/// <remarks>
/// The canonical form is unambiguous by construction — documents in ordinal path order, object
/// members ordinal-sorted, arrays in index order, every string and count length-prefixed, numbers
/// written as a normalised invariant decimal — so the same content stamps identically on any
/// machine. This is a separate encoding from <c>Core</c>'s <c>CanonicalStateWriter</c>; the two
/// never share bytes.
/// <para>
/// The stamp identifies content, not formatting: reindenting a data file does not move it, but
/// changing any key or value does.
/// </para>
/// </remarks>
public static class ContentHashing
{
    // Internal rather than private: ContentBundle READS this encoding, and a reader working from
    // its own copy of these numbers is a second definition of the format that can drift from the
    // one the stamp is taken over.
    internal const byte TagUnauthorised = 0x00;
    internal const byte TagFalse = 0x01;
    internal const byte TagTrue = 0x02;
    internal const byte TagNumber = 0x03;
    internal const byte TagText = 0x04;
    internal const byte TagArray = 0x05;
    internal const byte TagObject = 0x06;

    /// <summary>Version of the canonical encoding, mixed into the hash.</summary>
    public const byte CanonicalFormatVersion = 1;

    /// <summary>The canonical bytes the stamp is taken over. Exposed so a test can pin the encoding.</summary>
    public static byte[] CanonicalBytes(IEnumerable<ContentDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var ordered = documents.OrderBy(d => d.Path, StringComparer.Ordinal).ToArray();

        var buffer = new MemoryStream();
        buffer.WriteByte(CanonicalFormatVersion);
        WriteCount(buffer, ordered.Length);

        foreach (var document in ordered)
        {
            WriteString(buffer, document.Path);
            WriteValue(buffer, document.Root);
        }

        return buffer.ToArray();
    }

    /// <summary>The deterministic stamp of a document set.</summary>
    public static ContentVersion Compute(IEnumerable<ContentDocument> documents) =>
        ContentVersion.FromHex(Convert.ToHexString(SHA256.HashData(CanonicalBytes(documents))).ToLowerInvariant());

    private static void WriteValue(MemoryStream buffer, ContentValue value)
    {
        switch (value.Kind)
        {
            case ContentValueKind.Unauthorised:
                buffer.WriteByte(TagUnauthorised);
                break;

            case ContentValueKind.Boolean:
                buffer.WriteByte(value.AsBoolean() ? TagTrue : TagFalse);
                break;

            case ContentValueKind.Number:
                buffer.WriteByte(TagNumber);
                WriteString(buffer, Normalise(value.AsNumber()));
                break;

            case ContentValueKind.Text:
                buffer.WriteByte(TagText);
                WriteString(buffer, value.AsText());
                break;

            case ContentValueKind.Array:
                buffer.WriteByte(TagArray);
                WriteCount(buffer, value.Items.Count);
                foreach (var item in value.Items)
                {
                    WriteValue(buffer, item);
                }

                break;

            case ContentValueKind.Object:
                buffer.WriteByte(TagObject);
                WriteCount(buffer, value.MemberNames.Count);
                foreach (var name in value.MemberNames)
                {
                    WriteString(buffer, name);
                    value.TryGetMember(name, out var member);
                    WriteValue(buffer, member!);
                }

                break;

            // Closed allowlist with a throwing default: a new ContentValueKind must add its tag
            // and bytes here deliberately, rather than silently encoding as an object.
            default:
                throw new ArgumentOutOfRangeException(
                    nameof(value), value.Kind,
                    "ContentHashing has no canonical encoding for this kind. Adding a " +
                    "ContentValueKind means adding its tag and its bytes here, deliberately, in " +
                    "the same commit — and bumping CanonicalFormatVersion if any stamp moves.");
        }
    }

    /// <summary>
    /// A decimal written so that 1.5 and 1.500 produce identical bytes, and -0 is 0.
    /// </summary>
    private static string Normalise(decimal value)
    {
        var text = value.ToString(CultureInfo.InvariantCulture);

        if (text.Contains('.', StringComparison.Ordinal))
        {
            text = text.TrimEnd('0').TrimEnd('.');
        }

        return text is "-0" or "" ? "0" : text;
    }

    /// <summary>UTF-8 without a BOM, stated explicitly rather than inherited from a shared default.</summary>
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private static void WriteString(MemoryStream buffer, string value)
    {
        var bytes = Utf8.GetBytes(value);
        WriteCount(buffer, bytes.Length);
        buffer.Write(bytes);
    }

    private static void WriteCount(MemoryStream buffer, int count)
    {
        Span<byte> encoded = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(encoded, count);
        buffer.Write(encoded);
    }
}
