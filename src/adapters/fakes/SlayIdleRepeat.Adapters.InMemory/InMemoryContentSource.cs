using System.Text;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The in-memory fake for <see cref="IContentSourcePort"/>.
/// </summary>
/// <remarks>
/// Backed by a dictionary of bytes so a test can build a data tree without a filesystem;
/// <see cref="Revision"/> moves on every write to keep the hot-reload path testable.
/// </remarks>
public sealed class InMemoryContentSource : IContentSourcePort
{
    private readonly SortedDictionary<string, byte[]> _documents = new(StringComparer.Ordinal);
    private long _revision;

    /// <inheritdoc/>
    public string Revision => _revision.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Writes (or replaces) a document from a UTF-8 string, and moves the revision.</summary>
    public InMemoryContentSource Set(string documentPath, string utf8Text) =>
        Set(documentPath, Encoding.UTF8.GetBytes(utf8Text));

    /// <summary>Writes (or replaces) a document, and moves the revision.</summary>
    public InMemoryContentSource Set(string documentPath, byte[] bytes)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentPath);
        ArgumentNullException.ThrowIfNull(bytes);

        // Copied so a caller mutating its array afterwards can't change the source without moving Revision.
        _documents[documentPath] = bytes.ToArray();
        _revision++;
        return this;
    }

    /// <summary>Removes a document, and moves the revision.</summary>
    public InMemoryContentSource Remove(string documentPath)
    {
        if (_documents.Remove(documentPath))
        {
            _revision++;
        }

        return this;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> ListDocuments() => _documents.Keys.ToArray();

    /// <inheritdoc/>
    /// <remarks>
    /// Throws <see cref="SlayIdleRepeat.Core.Content.MissingContentException"/> rather than the
    /// dictionary's own <c>KeyNotFoundException</c>, so tests see the same failure as the real adapter.
    /// </remarks>
    public ReadOnlyMemory<byte> ReadDocument(string documentPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(documentPath);

        return _documents.TryGetValue(documentPath, out var bytes)
            ? bytes
            : throw new SlayIdleRepeat.Core.Content.MissingContentException(
                documentPath, $"this source holds {_documents.Count} document(s) and none of them is that one");
    }
}
