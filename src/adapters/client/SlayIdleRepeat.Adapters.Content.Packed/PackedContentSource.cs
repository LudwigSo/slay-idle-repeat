using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Adapters.Content.Packed;

/// <summary>
/// Reads the content set out of a packed artefact — a <c>.pck</c> beside a desktop executable, or
/// the assets of an APK. The <see cref="IContentSourcePort"/> an exported build boots on.
/// </summary>
/// <remarks>
/// <para>
/// 🔴 <b>It exists because its sibling cannot work in a shipped build.</b>
/// <c>LocalFileContentSource</c> reads <c>game-data</c> with <c>System.IO</c>, which is correct in a
/// checkout and in the editor and impossible once the content is packed: M7-10x finding (1),
/// measured in M7-10 against a real export — the desktop build succeeded and then died at
/// <c>AppRoot</c> with no content at all, because <c>res://data</c> globalises to a path that is not
/// on disk. Everything engine-specific about the fix is behind
/// <see cref="IPackedDocumentReader"/>; this half is plain C# and is contract-fixtured like every
/// other port implementation.
/// </para>
/// <para>
/// Like its sibling it does nothing but enumerate and read: no parsing, no validation, no caching
/// policy. That all lives one layer up where it can be unit-tested.
/// </para>
/// <para>
/// <b>Read-only, structurally.</b> The port declares no write member, the reader it depends on
/// declares no write member, and a packed artefact is not writable in the first place.
/// </para>
/// </remarks>
public sealed class PackedContentSource : IContentSourcePort
{
    /// <summary>The separator every document path in the content set uses.</summary>
    private const char PathSeparator = '/';

    /// <summary>The segment that would climb out of the content root.</summary>
    private const string ParentSegment = "..";

    private readonly IPackedDocumentReader _reader;

    /// <summary>Creates a source over whatever can read inside the packed artefact.</summary>
    /// <param name="reader">The engine-backed reader the composition root supplied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is null.</exception>
    public PackedContentSource(IPackedDocumentReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// 🔒 <b>Hashed over the bytes, and not over paths and sizes.</b> Its sibling derives a revision
    /// from paths, sizes and last-write times, and says in its own remarks that hashing is what to do
    /// if that heuristic is ever found wanting. Here it is not a preference: <b>a packed artefact has
    /// no last-write times to read</b>, so the mtime term would silently vanish and two different
    /// documents of equal length would produce one revision — a hot reload that does nothing, which
    /// is the exact failure a revision exists to prevent.
    /// </para>
    /// <para>
    /// ⚠️ It therefore reads every document. That is affordable for the reason it would not be on a
    /// server: the whole content set is a few dozen documents and a megabyte or so, the caller reads
    /// all of it at boot anyway, and a packed artefact cannot change while it is mounted — so in a
    /// shipped build this is asked once and can never answer differently.
    /// </para>
    /// </remarks>
    public string Revision
    {
        get
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var path in Sorted())
            {
                // The path and the length go in beside the bytes so that moving a document, or
                // truncating one to a prefix of another, both move the revision as well.
                hash.AppendData(Encoding.UTF8.GetBytes(path));
                hash.AppendData(Separator);

                // A document that vanished between the listing and the read hashes as empty rather
                // than throwing: Revision answers about the artefact as a whole, and a source that
                // threw while being asked whether anything had changed would take down a caller that
                // was only checking.
                _ = _reader.TryRead(path, out var bytes);

                hash.AppendData(
                    Encoding.UTF8.GetBytes(bytes.Length.ToString(CultureInfo.InvariantCulture)));
                hash.AppendData(Separator);
                hash.AppendData(bytes.Span);
                hash.AppendData(Separator);
            }

            return Convert.ToHexString(hash.GetHashAndReset());
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ListDocuments() => Sorted();

    /// <inheritdoc/>
    /// <remarks>
    /// 🔒 <b>Every failure is <see cref="MissingContentException"/>, which is the port's declared
    /// answer</b> — absent, escaping, or listed-but-unreadable alike, so a caller behaves the same
    /// against this adapter, the filesystem one and the fake. An empty path stays an
    /// <c>ArgumentException</c>: that is a caller defect rather than a document that is not there.
    /// </remarks>
    public ReadOnlyMemory<byte> ReadDocument(string documentPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentPath);

        // 🔒 Checked before the read and independently of what the artefact lists. A packed reader
        // has no filesystem to canonicalise a path against, so there is no Path.GetFullPath here to
        // turn "tuning/../../outside.json" into something a prefix test could catch — the segments
        // are the only signal, and refusing them outright is what keeps a document path a key inside
        // the content set rather than a way out of it.
        if (Escapes(documentPath))
        {
            throw new MissingContentException(
                documentPath,
                "it escapes the content root, so it is not a document this source lists. A document " +
                "path is a key inside the content set, never a way out of it.");
        }

        if (!_reader.TryRead(documentPath, out var bytes))
        {
            throw new MissingContentException(
                documentPath, "the packed content set holds no such document");
        }

        return bytes;
    }

    /// <summary>One byte between the parts of the revision, so two fields cannot run together.</summary>
    private static ReadOnlySpan<byte> Separator => ":"u8;

    /// <summary>
    /// Whether a path would climb out of the content root, or is not relative to it at all.
    /// </summary>
    private static bool Escapes(string documentPath) =>
        documentPath.StartsWith(PathSeparator) ||
        documentPath.Contains(':', StringComparison.Ordinal) ||
        documentPath.Split(PathSeparator).Contains(ParentSegment, StringComparer.Ordinal);

    /// <summary>
    /// The reader's documents in ordinal order, because the loader's determinism is stated over it.
    /// </summary>
    /// <remarks>
    /// 🔒 Sorted HERE rather than trusted from the reader. A directory walk inside a packed archive
    /// yields whatever order the archive's index happens to hold, which is not a promise any engine
    /// makes — and an unsorted list would make the content snapshot's stamp depend on how the
    /// exporter laid out the file, which is exactly the kind of thing that differs between two
    /// builds of identical content.
    /// </remarks>
    private string[] Sorted()
    {
        var documents = _reader.EnumerateDocuments().ToArray();

        Array.Sort(documents, StringComparer.Ordinal);

        return documents;
    }
}
