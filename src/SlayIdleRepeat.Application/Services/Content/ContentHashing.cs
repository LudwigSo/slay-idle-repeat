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
/// <para>
/// 🔒 The canonical form is unambiguous by construction — documents in ordinal path order, object
/// members ordinal-sorted, arrays in index order, every string and count length-prefixed, numbers
/// written as a normalised invariant decimal. No locale, no hash-table iteration order and no
/// floating-point formatting can reach it, so the same content stamps identically on a Windows
/// dev box, a Linux CI runner and an Android device. `14` §16.6's snapshot hashing is built on
/// this later; a non-deterministic ordering here would break it silently rather than loudly.
/// </para>
/// <para>
/// The stamp identifies <em>content</em>, not <em>formatting</em>: reindenting a data file does
/// not move it, changing any key or value does. That is the property `14` §6 actually needs —
/// "a replayed command reproduces its original outcome" is about values, and a stamp that moved
/// on whitespace would force a spurious content download after a formatting commit.
/// </para>
/// </remarks>
public static class ContentHashing
{
    private const byte TagUnauthorised = 0x00;
    private const byte TagFalse = 0x01;
    private const byte TagTrue = 0x02;
    private const byte TagNumber = 0x03;
    private const byte TagText = 0x04;
    private const byte TagArray = 0x05;
    private const byte TagObject = 0x06;

    /// <summary>
    /// Version of the canonical encoding, mixed into the hash. Bumping it moves every stamp, so it
    /// changes only when the encoding itself changes — deliberately, never as a side effect.
    /// </summary>
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

            default:
                buffer.WriteByte(TagObject);
                WriteCount(buffer, value.MemberNames.Count);
                foreach (var name in value.MemberNames)
                {
                    WriteString(buffer, name);
                    value.TryGetMember(name, out var member);
                    WriteValue(buffer, member!);
                }

                break;
        }
    }

    /// <summary>
    /// A decimal written so that 1.5 and 1.500 — the same number, differently typed by a human —
    /// produce identical bytes, and -0 is 0.
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

    private static void WriteString(MemoryStream buffer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
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
