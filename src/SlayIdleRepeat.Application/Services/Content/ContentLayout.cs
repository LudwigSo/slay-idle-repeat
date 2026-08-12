namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// The <c>game-data</c> directory convention, in one place.
/// </summary>
/// <remarks>
/// From <c>game-data/README.md</c>: <c>schema/</c> holds JSON Schema and never content;
/// <c>loc/*.json</c> is governed by <c>schema/loc.schema.json</c>; <c>tuning/experiments/*.json</c>
/// are override patches and 🔒 never part of a snapshot; each <c>content/&lt;type&gt;/</c> directory
/// is governed by the one schema for that content <em>type</em>; everything else is governed by
/// <c>schema/&lt;stem&gt;.schema.json</c>.
/// <para>
/// It is a type rather than a set of private constants because <c>tools/ContentValidator</c> needs
/// the same convention: two copies in two projects that must agree for CI to mean anything is one
/// copy too many.
/// </para>
/// </remarks>
public static class ContentLayout
{
    /// <summary>Where the JSON Schemas live.</summary>
    public const string SchemaDirectory = "schema/";

    /// <summary>Where the locale files live.</summary>
    public const string LocaleDirectory = "loc/";

    /// <summary>Where the 16 canonical tunable files of `21` §3.1 live.</summary>
    public const string TuningDirectory = "tuning/";

    /// <summary>🔒 Sparse override patches. Never shipped, never part of a snapshot.</summary>
    public const string ExperimentDirectory = "tuning/experiments/";

    /// <summary>The suffix that marks a schema.</summary>
    public const string SchemaSuffix = ".schema.json";

    /// <summary>The one schema that governs every locale file.</summary>
    public const string LocaleSchema = SchemaDirectory + "loc" + SchemaSuffix;

    /// <summary>Where the content-type directories live.</summary>
    public const string ContentDirectory = "content/";

    /// <summary>
    /// 🔒 Content is paired by <b>directory</b>, because content is paired by <em>type</em>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>game-data/README.md</c> declares 15 content directories, each holding many files
    /// of one type: <c>content/chapters/CH_01_EMBERFALL.json</c>, <c>CH_02_….json</c>, … all
    /// governed by the single <c>schema/chapter.schema.json</c> whose own <c>title</c> is
    /// <c>content/chapters/*.json</c>. The <see cref="SchemaFor"/> stem rule would look for
    /// <c>schema/CH_01_EMBERFALL.schema.json</c> per file — one <c>MissingSchema</c> per chapter, and
    /// a content-type schema that governs nothing.
    /// </para>
    /// <para>
    /// 🔒 A <b>declared table</b> rather than a smarter stemming rule, because
    /// <c>content/liveops_events/</c> → <c>event.schema.json</c> is not derivable by any rule: the
    /// README is explicit that <c>event.schema.json</c> (the path `26` §2 names) and
    /// <c>events.schema.json</c> (the framework-wide tuning file) differ by one letter and that the
    /// difference is load-bearing.
    /// </para>
    /// <para>
    /// A content directory with no entry here still resolves through the stem rule and therefore
    /// still fails loudly the day its first file lands — which is the point. Authoring a content
    /// type means authoring its schema <em>and</em> naming it here, in the same commit.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> ContentTypeSchemas { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // 14 §6 (whose worked example IS a chapter definition) / 19 — authored by M3-14.
            [ContentDirectory + "chapters/"] = SchemaDirectory + "chapter" + SchemaSuffix,

            // 26 §2 — one live-ops event package per file. Authored by M13-06.
            [ContentDirectory + "liveops_events/"] = SchemaDirectory + "event" + SchemaSuffix,

            // 🔴 The two directories that already hold content, added late — and the reason they were
            // easy to forget is worth stating. Both aggregate their type into ONE file named after
            // its own directory, so the stem rule answered correctly without an entry and nothing
            // failed loudly. The remarks above promise that a content directory with no entry here
            // "still fails loudly the day its first file lands"; for a single-document type named
            // after its directory, that promise is simply not kept, and the gap is invisible until a
            // SECOND file arrives — content/bosses/BOSS_THORNMAW.json would resolve to
            // schema/BOSS_THORNMAW.schema.json and report a missing schema for a type that has one.
            // Naming them here costs two lines and makes the declared table match the tree.

            // 05 §6-6.2 — the archetype rows, biome statuses and elite assignments. Authored by M2-11.
            [ContentDirectory + "enemies/"] = SchemaDirectory + "enemies" + SchemaSuffix,

            // 17 §1.2 / §2-9 — the eight boss scripts and the FTUE row. Authored by M2-13.
            [ContentDirectory + "bosses/"] = SchemaDirectory + "bosses" + SchemaSuffix,
        };

    /// <summary>True for a document under <c>schema/</c>.</summary>
    public static bool IsSchema(string documentPath) =>
        documentPath.StartsWith(SchemaDirectory, StringComparison.Ordinal);

    /// <summary>True for one of the canonical tuning files — not for an experiment override.</summary>
    public static bool IsTuningFile(string documentPath) =>
        documentPath.StartsWith(TuningDirectory, StringComparison.Ordinal) &&
        !documentPath.StartsWith(ExperimentDirectory, StringComparison.Ordinal);

    /// <summary>True for an override patch, which never enters a snapshot.</summary>
    public static bool IsExperiment(string documentPath) =>
        documentPath.StartsWith(ExperimentDirectory, StringComparison.Ordinal);

    /// <summary>The schema that governs a data document.</summary>
    /// <remarks>
    /// The declared tables come first — <c>loc/</c>, then <see cref="ContentTypeSchemas"/> — and the
    /// <c>schema/&lt;stem&gt;.schema.json</c> convention is the fallback, which is exactly the 1:1
    /// relationship <c>tuning/</c> has and content does not.
    /// </remarks>
    public static string SchemaFor(string documentPath)
    {
        ArgumentNullException.ThrowIfNull(documentPath);

        if (documentPath.StartsWith(LocaleDirectory, StringComparison.Ordinal))
        {
            return LocaleSchema;
        }

        foreach (var (directory, schema) in ContentTypeSchemas)
        {
            if (documentPath.StartsWith(directory, StringComparison.Ordinal))
            {
                return schema;
            }
        }

        return SchemaDirectory + StemOf(documentPath) + SchemaSuffix;
    }

    /// <summary>The bare data file name a schema governs, e.g. <c>forge.json</c>.</summary>
    public static string DataFileNameGovernedBy(string schemaPath) =>
        StemOf(schemaPath.Replace(SchemaSuffix, ".json", StringComparison.Ordinal)) + ".json";

    private static string StemOf(string documentPath)
    {
        var slash = documentPath.LastIndexOf('/');
        var name = slash < 0 ? documentPath : documentPath[(slash + 1)..];
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[..dot];
    }
}
