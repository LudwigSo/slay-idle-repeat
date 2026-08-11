namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// The <c>SlayIdleRepeat.Data</c> directory convention, in one place.
/// </summary>
/// <remarks>
/// From <c>SlayIdleRepeat.Data/README.md</c>: <c>schema/</c> holds JSON Schema and never content;
/// <c>loc/*.json</c> is governed by <c>schema/loc.schema.json</c>; <c>tuning/experiments/*.json</c>
/// are override patches and 🔒 never part of a snapshot; everything else is governed by
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
    public static string SchemaFor(string documentPath) =>
        documentPath.StartsWith(LocaleDirectory, StringComparison.Ordinal)
            ? LocaleSchema
            : SchemaDirectory + StemOf(documentPath) + SchemaSuffix;

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
