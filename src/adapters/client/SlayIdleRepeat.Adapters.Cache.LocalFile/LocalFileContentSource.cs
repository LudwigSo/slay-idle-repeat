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
    /// <summary>Creates a source over a data directory (the <c>SlayIdleRepeat.Data</c> root).</summary>
    public LocalFileContentSource(string dataRootPath) => throw new NotImplementedException();

    /// <summary>The data root this source reads.</summary>
    public string DataRootPath => throw new NotImplementedException();

    /// <inheritdoc/>
    public string Revision => throw new NotImplementedException();

    /// <inheritdoc/>
    public IReadOnlyList<string> ListDocuments() => throw new NotImplementedException();

    /// <inheritdoc/>
    public ReadOnlyMemory<byte> ReadDocument(string documentPath) => throw new NotImplementedException();
}
