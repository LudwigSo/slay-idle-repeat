namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// 🔒 The loaded, validated, version-stamped content, immutable for its whole life.
/// </summary>
/// <remarks>
/// <para>
/// `14` §6 and `30` §3: <em>"Content is loaded once into an immutable, version-stamped
/// <c>ContentSnapshot</c> and passed to the domain on <c>GameContext</c>. Loading JSON is I/O
/// and belongs in an adapter; reading content is a rule."</em> This type is the "reading" half:
/// no I/O, no parsing, no mutation, and no way to construct one that has not been stamped.
/// </para>
/// <para>
/// Hot reload does not mutate a snapshot — it builds a new one and swaps the reference. There is
/// no setter anywhere on this type for exactly that reason: a snapshot handed to a command must
/// still describe the same content when that command is replayed.
/// </para>
/// </remarks>
public sealed class ContentSnapshot
{
    private readonly Dictionary<string, ContentDocument> _documents;
    private readonly string[] _paths;

    /// <summary>Builds a snapshot from already-parsed, already-validated documents.</summary>
    /// <param name="version">The deterministic stamp of <paramref name="documents"/>.</param>
    /// <param name="documents">The documents. Duplicate paths throw.</param>
    public ContentSnapshot(ContentVersion version, IEnumerable<ContentDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(documents);

        Version = version;
        _documents = new Dictionary<string, ContentDocument>(StringComparer.Ordinal);

        foreach (var document in documents)
        {
            if (!_documents.TryAdd(document.Path, document))
            {
                throw new ArgumentException(
                    $"Document '{document.Path}' was supplied twice. One of the two would win " +
                    "silently, and which one is an ordering accident.",
                    nameof(documents));
            }
        }

        _paths = _documents.Keys.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    /// <summary>🔒 The deterministic content hash this snapshot was stamped with.</summary>
    public ContentVersion Version { get; }

    /// <summary>Every document path, ordinal-sorted.</summary>
    public IReadOnlyList<string> DocumentPaths => _paths;

    /// <summary>Looks a document up by path.</summary>
    public bool TryGetDocument(string documentPath, out ContentDocument? document) =>
        _documents.TryGetValue(documentPath, out document);

    /// <summary>Gets a document by path, or throws <see cref="MissingContentException"/>.</summary>
    public ContentDocument GetDocument(string documentPath) =>
        _documents.TryGetValue(documentPath, out var document)
            ? document
            : throw new MissingContentException(
                documentPath,
                $"this snapshot holds {_paths.Length} document(s) and none of them is that one");

    /// <summary>
    /// Resolves a reference such as <c>tuning/forge.json#/merge/inputCount</c>.
    /// Throws <see cref="MissingContentException"/> when nothing is there.
    /// </summary>
    /// <remarks>
    /// 🔒 This returns an <see cref="ContentValueKind.Unauthorised"/> value rather than throwing
    /// when the leaf is <c>null</c> — reading the <em>shape</em> of an unauthorised hole is
    /// legitimate (that is how a caller checks). Reading its <em>value</em> is not; that is what
    /// the typed readers below refuse to do.
    /// </remarks>
    public ContentValue Read(string reference)
    {
        var parsed = ContentReference.Parse(reference);
        var document = GetDocument(parsed.DocumentPath);
        var current = document.Root;

        foreach (var segment in parsed.Segments)
        {
            current = Step(current, segment)
                ?? throw new MissingContentException(reference, $"'{segment}' resolves to nothing");
        }

        return current;
    }

    /// <summary>Resolves a reference, or returns false when nothing is there.</summary>
    public bool TryRead(string reference, out ContentValue? value)
    {
        value = null;
        if (!ContentReference.TryParse(reference, out var parsed) ||
            !_documents.TryGetValue(parsed!.DocumentPath, out var document))
        {
            return false;
        }

        var current = document.Root;
        foreach (var segment in parsed.Segments)
        {
            var next = Step(current, segment);
            if (next is null)
            {
                return false;
            }

            current = next;
        }

        value = current;
        return true;
    }

    /// <summary>
    /// 🔒 True when the reference exists <em>and</em> the design docs authorised a value for it.
    /// The one sanctioned way to ask "may I read this?" without triggering the throw.
    /// </summary>
    public bool IsAuthorised(string reference) =>
        TryRead(reference, out var value) && !value!.IsUnauthorised;

    /// <summary>Reads a text value. Throws on absent, unauthorised, or wrong kind.</summary>
    public string ReadText(string reference) => Read(reference).AsText(reference);

    /// <summary>Reads an exact decimal. Throws on absent, unauthorised, or wrong kind.</summary>
    public decimal ReadNumber(string reference) => Read(reference).AsNumber(reference);

    /// <summary>Reads a double. Throws on absent, unauthorised, or wrong kind.</summary>
    public double ReadDouble(string reference) => Read(reference).AsDouble(reference);

    /// <summary>Reads a 32-bit integer. Throws on absent, unauthorised, wrong kind, or a fraction.</summary>
    public int ReadInt32(string reference) => Read(reference).AsInt32(reference);

    /// <summary>Reads a 64-bit integer. Throws on absent, unauthorised, wrong kind, or a fraction.</summary>
    public long ReadInt64(string reference) => Read(reference).AsInt64(reference);

    /// <summary>Reads a boolean. Throws on absent, unauthorised, or wrong kind.</summary>
    public bool ReadBoolean(string reference) => Read(reference).AsBoolean(reference);

    private static ContentValue? Step(ContentValue current, string segment)
    {
        if (current.Kind == ContentValueKind.Object)
        {
            return current.TryGetMember(segment, out var member) ? member : null;
        }

        if (current.Kind == ContentValueKind.Array &&
            int.TryParse(segment, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var index) &&
            index < current.Items.Count)
        {
            return current.Items[index];
        }

        return null;
    }
}
