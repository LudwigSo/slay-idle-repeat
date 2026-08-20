namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// The <c>game-data</c> directory convention, in one place.
/// </summary>
/// <remarks>
/// <c>schema/</c> holds JSON Schema and never content; <c>loc/*.json</c> is governed by
/// <c>schema/loc.schema.json</c>; <c>tuning/experiments/*.json</c> are override patches and never
/// part of a snapshot; each <c>content/&lt;type&gt;/</c> directory is governed by the one schema
/// for that content type; everything else is governed by <c>schema/&lt;stem&gt;.schema.json</c>.
/// <para>
/// It is a type rather than a set of private constants because <c>tools/ContentValidator</c> needs
/// the same convention — two copies in two projects that must agree is one copy too many.
/// </para>
/// </remarks>
public static class ContentLayout
{
    /// <summary>Where the JSON Schemas live.</summary>
    public const string SchemaDirectory = "schema/";

    /// <summary>Where the locale files live.</summary>
    public const string LocaleDirectory = "loc/";

    /// <summary>Where the canonical tunable files live.</summary>
    public const string TuningDirectory = "tuning/";

    /// <summary>Sparse override patches. Never shipped, never part of a snapshot.</summary>
    public const string ExperimentDirectory = "tuning/experiments/";

    /// <summary>The suffix that marks a schema.</summary>
    public const string SchemaSuffix = ".schema.json";

    /// <summary>The one schema that governs every locale file.</summary>
    public const string LocaleSchema = SchemaDirectory + "loc" + SchemaSuffix;

    /// <summary>Where the content-type directories live.</summary>
    public const string ContentDirectory = "content/";

    /// <summary>
    /// Content is paired by <b>directory</b>, because content is paired by <em>type</em>: many files
    /// of one type share a single schema whose own name does not match the stem convention.
    /// </summary>
    /// <remarks>
    /// A declared table rather than a smarter stemming rule, because some pairings (e.g. plural vs.
    /// singular file stems, or a content type whose schema name differs from its directory by a
    /// single letter) are not derivable by any general rule.
    /// <para>
    /// A content directory with no entry here still resolves through the stem rule and therefore
    /// still fails loudly the day its first file lands — which is the point. Authoring a content
    /// type means authoring its schema <em>and</em> naming it here, in the same commit. A
    /// single-document type named after its own directory is the easy case to forget: the stem rule
    /// answers correctly with no entry until a second file arrives, at which point the gap becomes
    /// visible only as a wrongly-resolved schema path.
    /// </para>
    /// </remarks>
    public static IReadOnlyDictionary<string, string> ContentTypeSchemas { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ContentDirectory + "chapters/"] = SchemaDirectory + "chapter" + SchemaSuffix,

            [ContentDirectory + "liveops_events/"] = SchemaDirectory + "event" + SchemaSuffix,

            [ContentDirectory + "enemies/"] = SchemaDirectory + "enemies" + SchemaSuffix,

            [ContentDirectory + "bosses/"] = SchemaDirectory + "bosses" + SchemaSuffix,

            [ContentDirectory + "curses/"] = SchemaDirectory + "curses" + SchemaSuffix,

            // Named explicitly although the stem rule already answers correctly for the single
            // gear.json this directory holds today — the remarks above call that the easy case to
            // forget, because the gap only becomes visible the day a second file arrives.
            [ContentDirectory + "gear/"] = SchemaDirectory + "gear" + SchemaSuffix,

            // The set-bonus effects. A directory of its own rather than a second file under gear/,
            // because that row already binds every file under gear/ to the base-item grid's schema.
            [ContentDirectory + "sets/"] = SchemaDirectory + "sets" + SchemaSuffix,

            // Named explicitly (schema/perk.schema.json, singular) because the stem of
            // content/perks/perks.json is "perks" (plural) and the bare stem rule cannot bridge
            // a one-letter difference.
            [ContentDirectory + "perks/"] = SchemaDirectory + "perk" + SchemaSuffix,

            // The one row where the naming hazard above is three-way: board_events.schema.json
            // (this row), event.schema.json (the live-ops event package) and events.schema.json
            // (the framework-wide tuning file) differ by a letter and a word.
            [ContentDirectory + "board_events/"] = SchemaDirectory + "board_events" + SchemaSuffix,

            // The name-filter word lists, one file per language. This row is REQUIRED rather than
            // convenient: the files are named after their language, so the stem rule would look for
            // schema/en.schema.json and schema/de.schema.json and find neither.
            [ContentDirectory + "profanity/"] = SchemaDirectory + "profanity" + SchemaSuffix,

            // The boot screen's string slots. Another instance of the easy case above: the stem rule
            // already answers correctly for the single boot.json here, so the row buys nothing today
            // and everything on the day a second document lands beside it.
            [ContentDirectory + "boot/"] = SchemaDirectory + "boot" + SchemaSuffix,

            // The Home screen's string slots — the same easy case as boot/ above.
            [ContentDirectory + "home/"] = SchemaDirectory + "home" + SchemaSuffix,

            // The Chapter Select screen's string slots — likewise.
            [ContentDirectory + "chapter_select/"] = SchemaDirectory + "chapter_select" + SchemaSuffix,

            // The Board screen's string slots — likewise.
            [ContentDirectory + "board/"] = SchemaDirectory + "board" + SchemaSuffix,

            // The Die Panel's string slots — likewise.
            [ContentDirectory + "die_panel/"] = SchemaDirectory + "die_panel" + SchemaSuffix,

            // The Battle Replay screen's string slots — likewise.
            [ContentDirectory + "battle/"] = SchemaDirectory + "battle" + SchemaSuffix,

            // The Perk Draft screen's string slots — likewise.
            [ContentDirectory + "perk_draft/"] = SchemaDirectory + "perk_draft" + SchemaSuffix,

            // The run Shop screen's string slots. The row here least likely to stay the easy case:
            // the offer catalogue this screen cannot yet stock lands beside this document.
            [ContentDirectory + "shop/"] = SchemaDirectory + "shop" + SchemaSuffix,

            // The Campfire / Shrine screen's string slots — one document serving two arms, and
            // splitting the shrine's half out is the tidy-up this row keeps buildable.
            [ContentDirectory + "campfire/"] = SchemaDirectory + "campfire" + SchemaSuffix,

            // The Dice Forge screen's string slots. `13` authors no layout section for this tile, so
            // the document is shaped like its three siblings rather than to a spec of its own.
            [ContentDirectory + "dice_forge/"] = SchemaDirectory + "dice_forge" + SchemaSuffix,
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
    /// <c>schema/&lt;stem&gt;.schema.json</c> convention is the fallback.
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
