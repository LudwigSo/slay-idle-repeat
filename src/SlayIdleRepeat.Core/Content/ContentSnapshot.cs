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
    /// <summary>Builds a snapshot from already-parsed, already-validated documents.</summary>
    /// <param name="version">The deterministic stamp of <paramref name="documents"/>.</param>
    /// <param name="documents">The documents. Duplicate paths throw.</param>
    public ContentSnapshot(ContentVersion version, IEnumerable<ContentDocument> documents) =>
        throw new NotImplementedException();

    /// <summary>🔒 The deterministic content hash this snapshot was stamped with.</summary>
    public ContentVersion Version => throw new NotImplementedException();

    /// <summary>Every document path, ordinal-sorted.</summary>
    public IReadOnlyList<string> DocumentPaths => throw new NotImplementedException();

    /// <summary>Looks a document up by path.</summary>
    public bool TryGetDocument(string documentPath, out ContentDocument? document) =>
        throw new NotImplementedException();

    /// <summary>Gets a document by path, or throws <see cref="MissingContentException"/>.</summary>
    public ContentDocument GetDocument(string documentPath) => throw new NotImplementedException();

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
    public ContentValue Read(string reference) => throw new NotImplementedException();

    /// <summary>Resolves a reference, or returns false when nothing is there.</summary>
    public bool TryRead(string reference, out ContentValue? value) => throw new NotImplementedException();

    /// <summary>
    /// 🔒 True when the reference exists <em>and</em> the design docs authorised a value for it.
    /// The one sanctioned way to ask "may I read this?" without triggering the throw.
    /// </summary>
    public bool IsAuthorised(string reference) => throw new NotImplementedException();

    /// <summary>Reads a text value. Throws on absent, unauthorised, or wrong kind.</summary>
    public string ReadText(string reference) => throw new NotImplementedException();

    /// <summary>Reads an exact decimal. Throws on absent, unauthorised, or wrong kind.</summary>
    public decimal ReadNumber(string reference) => throw new NotImplementedException();

    /// <summary>Reads a double. Throws on absent, unauthorised, or wrong kind.</summary>
    public double ReadDouble(string reference) => throw new NotImplementedException();

    /// <summary>Reads a 32-bit integer. Throws on absent, unauthorised, wrong kind, or a fraction.</summary>
    public int ReadInt32(string reference) => throw new NotImplementedException();

    /// <summary>Reads a 64-bit integer. Throws on absent, unauthorised, wrong kind, or a fraction.</summary>
    public long ReadInt64(string reference) => throw new NotImplementedException();

    /// <summary>Reads a boolean. Throws on absent, unauthorised, or wrong kind.</summary>
    public bool ReadBoolean(string reference) => throw new NotImplementedException();
}
