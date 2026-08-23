namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>What the content validator found.</summary>
public enum ContentIssueCode
{
    /// <summary>An id, key or enum member the schema does not recognise.</summary>
    UnknownId = 1,

    /// <summary>A definition names an icon that no icon manifest declares.</summary>
    MissingIcon = 2,

    /// <summary>A number outside the bounds its schema states.</summary>
    OutOfRange = 3,

    /// <summary>A reference to an id that exists nowhere in the content set.</summary>
    OrphanedReference = 4,

    /// <summary>The same id declared twice.</summary>
    DuplicateId = 5,

    /// <summary>The bytes are not RFC 8259 JSON.</summary>
    MalformedJson = 6,

    /// <summary>
    /// The same property name twice in one object. <c>System.Text.Json</c>'s object model keeps
    /// the last one silently, so this is a value that vanishes with no error anywhere.
    /// </summary>
    DuplicateKey = 7,

    /// <summary>A schema assertion failed that is none of the classes above.</summary>
    SchemaViolation = 8,

    /// <summary>A data file that no schema governs.</summary>
    MissingSchema = 9,

    /// <summary>A schema that governs no data file — it outlived its content.</summary>
    OrphanSchema = 10,

    /// <summary>
    /// A schema keyword this validator does not implement. Hard failure, never a silent pass — a
    /// permissive schema is worse than no schema, because it manufactures false confidence.
    /// </summary>
    UnsupportedSchemaKeyword = 11,

    /// <summary>An override patch names a key the canonical file does not have.</summary>
    OverrideTargetMissing = 12,

    /// <summary>The locale files do not carry an identical key set.</summary>
    LocalisationMismatch = 13,
}

/// <summary>One finding from the content validator.</summary>
/// <param name="Code">Which failure class this is.</param>
/// <param name="Location">Where it was found — a document path, or a document path plus pointer.</param>
/// <param name="Message">What is wrong, in a sentence somebody can act on.</param>
public sealed record ContentIssue(ContentIssueCode Code, string Location, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Code} · {Location} · {Message}";
}
