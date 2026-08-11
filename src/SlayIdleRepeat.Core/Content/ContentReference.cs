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
    private readonly string[] _segments;

    private ContentReference(string documentPath, string[] segments, string canonical)
    {
        DocumentPath = documentPath;
        _segments = segments;
        Canonical = canonical;
    }

    /// <summary>The snapshot-relative document path, e.g. <c>tuning/forge.json</c>.</summary>
    public string DocumentPath { get; }

    /// <summary>The unescaped pointer segments. Empty means the document root.</summary>
    public IReadOnlyList<string> Segments => _segments;

    /// <summary>The canonical <c>path#/a/b</c> form.</summary>
    public string Canonical { get; }

    /// <summary>Parses a reference. Throws <see cref="FormatException"/> on a malformed one.</summary>
    public static ContentReference Parse(string reference) =>
        TryParse(reference, out var result)
            ? result!
            : throw new FormatException(
                $"'{reference}' is not a content reference. The form is " +
                "'<document path>' or '<document path>#/<pointer>' (RFC 6901).");

    /// <summary>Parses a reference, or returns false.</summary>
    public static bool TryParse(string reference, out ContentReference? result)
    {
        result = null;
        if (string.IsNullOrEmpty(reference))
        {
            return false;
        }

        var hash = reference.IndexOf('#', StringComparison.Ordinal);
        if (hash == 0)
        {
            return false;
        }

        if (hash < 0)
        {
            result = new ContentReference(reference, [], reference);
            return true;
        }

        var documentPath = reference[..hash];
        var fragment = reference[(hash + 1)..];

        if (fragment.Length == 0)
        {
            result = new ContentReference(documentPath, [], documentPath);
            return true;
        }

        if (fragment[0] != '/')
        {
            return false;
        }

        var raw = fragment[1..].Split('/');
        var segments = new string[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            // RFC 6901 order matters: ~1 first, then ~0, or "~01" would decode to "/".
            segments[i] = raw[i]
                .Replace("~1", "/", StringComparison.Ordinal)
                .Replace("~0", "~", StringComparison.Ordinal);
        }

        result = new ContentReference(documentPath, segments, reference);
        return true;
    }

    /// <inheritdoc/>
    public bool Equals(ContentReference? other) =>
        other is not null && string.Equals(Canonical, other.Canonical, StringComparison.Ordinal);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ContentReference);

    /// <inheritdoc/>
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Canonical);

    /// <inheritdoc/>
    public override string ToString() => Canonical;
}
