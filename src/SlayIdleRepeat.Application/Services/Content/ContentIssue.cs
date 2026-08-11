namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// What the content validator found. The first five entries are `14` §6's own list, verbatim:
/// <em>"The build fails on unknown IDs, missing icons, out-of-range values, orphaned references
/// or duplicate IDs."</em>
/// </summary>
public enum ContentIssueCode
{
    /// <summary>`14` §6 — an id, key or enum member the schema does not recognise.</summary>
    UnknownId = 1,

    /// <summary>`14` §6 — a definition names an icon that no icon manifest declares.</summary>
    MissingIcon = 2,

    /// <summary>`14` §6 — a number outside the bounds its schema states.</summary>
    OutOfRange = 3,

    /// <summary>`14` §6 — a reference to an id that exists nowhere in the content set.</summary>
    OrphanedReference = 4,

    /// <summary>`14` §6 — the same id declared twice.</summary>
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

    /// <summary>A data file that no schema governs (`14` §6 — schemas govern everything).</summary>
    MissingSchema = 9,

    /// <summary>A schema that governs no data file — it outlived its content.</summary>
    OrphanSchema = 10,

    /// <summary>
    /// 🔒 A schema keyword this validator does not implement. Hard failure, never a silent pass:
    /// `SlayIdleRepeat.Data/README.md` — <em>"a permissive schema is worse than no schema,
    /// because it manufactures confidence."</em>
    /// </summary>
    UnsupportedSchemaKeyword = 11,

    /// <summary>An override patch names a key the canonical file does not have (`21` §3.3).</summary>
    OverrideTargetMissing = 12,

    /// <summary>The locale files do not carry an identical key set.</summary>
    LocalisationMismatch = 13,

    /// <summary>A 📐 marker in the docs that no schema key claims.</summary>
    TunableMarkerUnmatched = 14,

    /// <summary>A tuning schema key whose cited doc section carries no 📐 marker.</summary>
    TunableKeyUnmarked = 15,

    /// <summary>
    /// 🔒 An economy-affecting 📐 number that names a data file outside <c>tuning/</c>
    /// (`14` §6). Non-economy files are allow-listed explicitly; see
    /// <c>TunableMarkerAudit.NonEconomyDataFiles</c>.
    /// </summary>
    TunableOutsideTuningDirectory = 16,

    /// <summary>
    /// A baseline entry that no longer describes a real mismatch. Removing it is forced rather
    /// than remembered — the same mechanism <c>build/ci/test-suites.json</c> uses for empty suites.
    /// </summary>
    StaleBaselineEntry = 17,
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
