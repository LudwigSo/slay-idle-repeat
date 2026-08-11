namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// A pointer at one value inside a <see cref="ContentSnapshot"/>:
/// <c>tuning/forge.json#/merge/inputCount</c>.
/// </summary>
/// <remarks>
/// The fragment is an RFC 6901 JSON Pointer, so <c>~0</c> and <c>~1</c> escape <c>~</c> and
/// <c>/</c> in a member name. The document part is the snapshot-relative path with forward
/// slashes, exactly as <c>IContentSourcePort</c> lists it.
/// </remarks>
public sealed class ContentReference : IEquatable<ContentReference>
{
    /// <summary>The snapshot-relative document path, e.g. <c>tuning/forge.json</c>.</summary>
    public string DocumentPath => throw new NotImplementedException();

    /// <summary>The unescaped pointer segments. Empty means the document root.</summary>
    public IReadOnlyList<string> Segments => throw new NotImplementedException();

    /// <summary>The canonical <c>path#/a/b</c> form.</summary>
    public string Canonical => throw new NotImplementedException();

    /// <summary>Parses a reference. Throws <see cref="FormatException"/> on a malformed one.</summary>
    public static ContentReference Parse(string reference) => throw new NotImplementedException();

    /// <summary>Parses a reference, or returns false.</summary>
    public static bool TryParse(string reference, out ContentReference? result) =>
        throw new NotImplementedException();

    /// <inheritdoc/>
    public bool Equals(ContentReference? other) => throw new NotImplementedException();

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ContentReference);

    /// <inheritdoc/>
    public override int GetHashCode() => throw new NotImplementedException();

    /// <inheritdoc/>
    public override string ToString() => Canonical;
}
