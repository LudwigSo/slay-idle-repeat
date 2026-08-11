using System.Globalization;
using System.Text;
using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Adapters.Content.LocalFile;

/// <summary>
/// Reads <c>game-data</c> off the local filesystem — the real
/// <see cref="IContentSourcePort"/>.
/// </summary>
/// <remarks>
/// <para>
/// `14` §6: <em>"Loading JSON is I/O and belongs in an adapter."</em> This is the adapter. It does
/// nothing but enumerate and read: no parsing, no validation, no caching policy — everything that
/// could be wrong about the content is decided one layer up, where it can be unit-tested.
/// </para>
/// <para>
/// 🔒 <b>Read-only, structurally.</b> The port has no write member, and this class opens no stream
/// for writing. `21` §3.3's "the canonical data is only ever edited when a change is adopted" is
/// not a convention the loader is trusted to keep — there is simply no code path from a load to a
/// write.
/// </para>
/// <para>
/// <see cref="Revision"/> is derived from the paths, sizes and last-write times of the files, so a
/// dev hot-reload can tell "nothing changed" from "rebuild" without re-reading every byte.
/// </para>
/// </remarks>
public sealed class LocalFileContentSource : IContentSourcePort
{
    private const string Pattern = "*.json";

    /// <summary>Creates a source over a data directory (the <c>game-data</c> root).</summary>
    public LocalFileContentSource(string dataRootPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRootPath);

        DataRootPath = Path.GetFullPath(dataRootPath);

        if (!Directory.Exists(DataRootPath))
        {
            throw new DirectoryNotFoundException($"No content directory at '{DataRootPath}'.");
        }
    }

    /// <summary>The data root this source reads.</summary>
    public string DataRootPath { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// ⚠️ Paths, sizes and last-write ticks. On a filesystem with coarse timestamps an edit that
    /// changes neither the length nor the visible mtime produces an identical revision and the dev
    /// hot-reload silently does nothing — the failure this class exists to prevent. If that is ever
    /// observed, hash the bytes here rather than widening the heuristic.
    /// </remarks>
    public string Revision
    {
        get
        {
            var builder = new StringBuilder();
            foreach (var file in Files())
            {
                var info = new FileInfo(file);
                builder.Append(Relative(file)).Append(':')
                       .Append(info.Length.ToString(CultureInfo.InvariantCulture)).Append(':')
                       .Append(info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture)).Append(';');
            }

            return builder.ToString();
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ListDocuments() => Files().Select(Relative).ToArray();

    /// <inheritdoc/>
    /// <remarks>
    /// 🔒 One declared failure type for every way a path can fail to be a listed document —
    /// absent, a directory that does not exist, or an escape attempt. The port declares
    /// <see cref="MissingContentException"/>; letting <c>File.ReadAllBytes</c>'s
    /// <c>FileNotFoundException</c> and a path-escape <c>ArgumentException</c> out instead made
    /// this adapter fail three different ways, none of them the fake's.
    /// </remarks>
    public ReadOnlyMemory<byte> ReadDocument(string documentPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentPath);

        var absolute = Path.GetFullPath(Path.Combine(DataRootPath, documentPath));

        // A document path is a key inside the content set, never a way out of it. The trailing
        // separator matters: a bare prefix test lets a sibling directory `…Data-backup` through.
        var root = DataRootPath.EndsWith(Path.DirectorySeparatorChar)
            ? DataRootPath
            : DataRootPath + Path.DirectorySeparatorChar;

        if (!absolute.StartsWith(root, StringComparison.Ordinal))
        {
            throw new MissingContentException(
                documentPath,
                "it escapes the content root, so it is not a document this source lists. A document " +
                "path is a key inside the content set, never a way out of it.");
        }

        if (!File.Exists(absolute))
        {
            throw new MissingContentException(
                documentPath, $"there is no such file under '{DataRootPath}'");
        }

        return File.ReadAllBytes(absolute);
    }

    /// <summary>Ordinal-sorted, because the loader's determinism is stated over this order.</summary>
    private IEnumerable<string> Files() =>
        Directory.EnumerateFiles(DataRootPath, Pattern, SearchOption.AllDirectories)
                 .OrderBy(Relative, StringComparer.Ordinal);

    private string Relative(string absolutePath) =>
        Path.GetRelativePath(DataRootPath, absolutePath).Replace('\\', '/');
}
