using System.Text;
using SlayIdleRepeat.Application.Ports.Shared;

namespace SlayIdleRepeat.Adapters.InMemory;

/// <summary>
/// The in-memory fake for <see cref="IContentSourcePort"/> (`23` §5 A5).
/// </summary>
/// <remarks>
/// Content held as bytes in a dictionary, which is exactly what the real source hands over — so a
/// test can build a whole data tree, a malformed file, or a one-byte edit without a filesystem.
/// <see cref="Revision"/> moves on every write, which is what makes the hot-reload path testable
/// without a file watcher.
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

        // Copied: a test that mutates its array afterwards would otherwise change the source
        // without moving Revision, which is the one thing this fake exists to model faithfully.
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
    public ReadOnlyMemory<byte> ReadDocument(string documentPath) =>
        _documents.TryGetValue(documentPath, out var bytes)
            ? bytes
            : throw new KeyNotFoundException($"No content document '{documentPath}' in this source.");
}
