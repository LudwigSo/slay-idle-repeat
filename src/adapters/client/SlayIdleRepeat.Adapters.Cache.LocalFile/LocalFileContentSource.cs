using System.Globalization;
using System.Text;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Adapters.Cache.LocalFile;

/// <summary>
/// Reads <c>SlayIdleRepeat.Data</c> off the local filesystem — the real
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

    /// <summary>Creates a source over a data directory (the <c>SlayIdleRepeat.Data</c> root).</summary>
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
    public ReadOnlyMemory<byte> ReadDocument(string documentPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentPath);

        var absolute = Path.GetFullPath(Path.Combine(DataRootPath, documentPath));

        // A document path is a key inside the content set, never a way out of it.
        if (!absolute.StartsWith(DataRootPath, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{documentPath}' escapes the content root.", nameof(documentPath));
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
