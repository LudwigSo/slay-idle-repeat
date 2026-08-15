using SlayIdleRepeat.Application.Ports.Shared;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// Turns the bytes behind an <see cref="IContentSourcePort"/> into an immutable, version-stamped
/// <c>ContentSnapshot</c>: parse → layer overrides → validate against <c>game-data/schema/</c> →
/// check cross-file invariants → stamp.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic by construction. Documents are processed in ordinal path order, object members
/// are held ordinal-sorted, and the stamp is computed over a canonical serialisation — so the same
/// bytes always produce the same snapshot and the same stamp, on any machine.
/// </para>
/// <para>
/// The directory convention it reads:
/// <list type="bullet">
/// <item><c>schema/*.json</c> — JSON Schema, never content.</item>
/// <item><c>loc/*.json</c> — governed by <c>schema/loc.schema.json</c>.</item>
/// <item><c>tuning/experiments/*.json</c> — override patches. Never part of a snapshot; they load
/// only when named in <see cref="ContentLoadOptions.OverrideDocuments"/>.</item>
/// <item><c>content/&lt;type&gt;/*.json</c> — many files of one type, all governed by that type's
/// one schema, declared in <see cref="ContentLayout.ContentTypeSchemas"/>.</item>
/// <item>everything else — governed by <c>schema/&lt;stem&gt;.schema.json</c>.</item>
/// </list>
/// </para>
/// </remarks>
public static class ContentLoader
{
    /// <summary>
    /// The only schemas allowed to govern nothing, because the content they describe has an owner
    /// and a date rather than a doubt.
    /// </summary>
    /// <remarks>
    /// The exemption is self-expiring: a schema listed here that <em>does</em> govern a data file
    /// fails the build as a stale exemption, so removing the entry is forced rather than
    /// remembered.
    /// </remarks>
    public static IReadOnlyList<string> SchemasAwaitingContent { get; } =
    [
        // First authored live-ops event package (EVT_EMBERFALL) lands under this schema before
        // any event exists, so running an event never needs an app update.
        "schema/event.schema.json",
    ];

    /// <summary>
    /// Schemas that describe a <em>shape other schemas are written to</em>, not a file. They govern
    /// nothing permanently and by design, which is a different claim from
    /// <see cref="SchemasAwaitingContent"/>'s self-expiring one.
    /// </summary>
    /// <remarks>
    /// There is no mechanical expiry here. What guards it instead is
    /// <c>EffectSchemaTests.No_other_schema_restates_the_effect_vocabulary</c>, which fails on the
    /// commit that copies this shape into a content-type schema — <see cref="JsonSchemaValidator"/>
    /// resolves same-document pointers only, so no content schema can <c>$ref</c> across files.
    /// </remarks>
    public static IReadOnlyList<string> VocabularySchemas { get; } =
    [
        // The EffectDefinition shape. An effect is never a file: it is always embedded in the
        // perk, talent, pet, mount, curse or boss script that owns it.
        "schema/effect.schema.json",
    ];

    /// <summary>Loads the canonical content with no overrides.</summary>
    public static ContentLoadResult Load(IContentSourcePort source) => Load(source, ContentLoadOptions.Canonical);

    /// <summary>Loads content, layering the named sparse override patches.</summary>
    public static ContentLoadResult Load(IContentSourcePort source, ContentLoadOptions options)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);

        var issues = new List<ContentIssue>();
        var paths = source.ListDocuments().OrderBy(p => p, StringComparer.Ordinal).ToArray();

        if (paths.Length == 0)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.MissingSchema, "(content source)",
                "the source holds no documents. A validator that found nothing to validate has " +
                "not passed — it has been given nothing."));
            return new ContentLoadResult(null, issues);
        }

        var parsed = Parse(source, paths, issues);
        if (issues.Count > 0)
        {
            return new ContentLoadResult(null, issues);
        }

        var schemas = parsed
            .Where(d => ContentLayout.IsSchema(d.Key))
            .ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);

        var data = parsed
            .Where(d => !ContentLayout.IsSchema(d.Key) &&
                        !ContentLayout.IsExperiment(d.Key))
            .ToDictionary(d => d.Key, d => d.Value, StringComparer.Ordinal);

        if (schemas.Count == 0)
        {
            issues.Add(new ContentIssue(
                ContentIssueCode.MissingSchema, ContentLayout.SchemaDirectory,
                "holds no schema files, so no data file can be validated against anything (14 §6)."));
        }

        // Sweep each schema for unimplemented/malformed keywords ONCE, before any instance is
        // validated — not once per data file, and not only where an instance happens to walk. A
        // keyword in a branch no document reaches is still a keyword nobody is enforcing.
        var unusable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (schemaPath, schema) in schemas.OrderBy(s => s.Key, StringComparer.Ordinal))
        {
            var found = JsonSchemaValidator.CheckSchemaKeywords(schema, schemaPath);
            if (found.Count > 0)
            {
                // A schema that failed its own sweep is not a schema to validate against —
                // walking an instance through it would replace a located finding with a bare
                // stack trace.
                unusable.Add(schemaPath);
            }

            issues.AddRange(found);
        }

        ApplyOverrides(parsed, data, options, issues);

        var pairing = Pair(data, schemas, issues);
        var bindings = ValidateAgainstSchemas(data, schemas, pairing, unusable, issues);

        // The cross-file rules read schema-validated shapes: an already-rejected value reaching an
        // AsInt32() would throw, replacing a located, actionable finding with a bare stack trace.
        if (issues.Count == 0)
        {
            issues.AddRange(ContentInvariants.Check(data, bindings, schemas, options));
        }

        var ordered = issues
            .OrderBy(i => i.Location, StringComparer.Ordinal)
            .ThenBy(i => i.Code)
            .ThenBy(i => i.Message, StringComparer.Ordinal)
            .ToArray();

        if (ordered.Length > 0)
        {
            return new ContentLoadResult(null, ordered);
        }

        var documents = data
            .Select(d => new ContentDocument(d.Key, d.Value))
            .OrderBy(d => d.Path, StringComparer.Ordinal)
            .ToArray();

        return new ContentLoadResult(new ContentSnapshot(ContentHashing.Compute(documents), documents), ordered);
    }

    private static Dictionary<string, ContentValue> Parse(
        IContentSourcePort source, IReadOnlyList<string> paths, List<ContentIssue> issues)
    {
        var parsed = new Dictionary<string, ContentValue>(StringComparer.Ordinal);

        foreach (var path in paths)
        {
            if (JsonContentReader.TryRead(path, source.ReadDocument(path).Span, out var root, out var found))
            {
                parsed[path] = root!;
            }
            else
            {
                issues.AddRange(found);
            }
        }

        return parsed;
    }

    private static void ApplyOverrides(
        IReadOnlyDictionary<string, ContentValue> parsed,
        Dictionary<string, ContentValue> data,
        ContentLoadOptions options,
        List<ContentIssue> issues)
    {
        foreach (var overridePath in options.OverrideDocuments)
        {
            if (!parsed.TryGetValue(overridePath, out var patchRoot))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OverrideTargetMissing, overridePath,
                    "was named as an override but the content source does not hold it."));
                continue;
            }

            issues.AddRange(OverridePatch.Split(overridePath, patchRoot, data.Keys, out var patches));

            foreach (var (documentPath, patch) in patches.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                data[documentPath] = OverridePatch.Apply(data[documentPath], patch, documentPath, issues);
            }
        }
    }

    /// <summary>
    /// Every data file is governed by a schema, and every schema governs a data file. A schema
    /// that outlived its content is as much a defect as content nobody validates.
    /// </summary>
    private static Dictionary<string, string> Pair(
        IReadOnlyDictionary<string, ContentValue> data,
        IReadOnlyDictionary<string, ContentValue> schemas,
        List<ContentIssue> issues)
    {
        var pairing = new Dictionary<string, string>(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in data.Keys.OrderBy(p => p, StringComparer.Ordinal))
        {
            var expected = ContentLayout.SchemaFor(path);

            if (schemas.ContainsKey(expected))
            {
                pairing[path] = expected;
                used.Add(expected);
            }
            else
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.MissingSchema, path,
                    $"expects the schema '{expected}', which does not exist. 14 §6 validates every " +
                    "content file against a schema; an unvalidated file is one nobody is checking."));
            }
        }

        foreach (var schema in schemas.Keys.OrderBy(s => s, StringComparer.Ordinal))
        {
            var awaitingContent = SchemasAwaitingContent.Contains(schema, StringComparer.Ordinal);
            var vocabulary = VocabularySchemas.Contains(schema, StringComparer.Ordinal);

            if (awaitingContent && vocabulary)
            {
                // The two lists make opposite claims — "its content is coming" and "it will never
                // have content". A schema in both is a claim nobody has decided between.
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanSchema, schema,
                    "is in both ContentLoader.SchemasAwaitingContent and ContentLoader.VocabularySchemas. " +
                    "The first says its content has an owner and a date; the second says it describes a " +
                    "shape and will never govern a file. Pick one."));
            }

            if (!used.Contains(schema) && !awaitingContent && !vocabulary)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanSchema, schema,
                    "governs no data file. Either the data it describes is missing, or the schema " +
                    "outlived its content and should be deleted. If the content has an owner and a " +
                    "milestone, say so in ContentLoader.SchemasAwaitingContent; if it describes a " +
                    "shape other schemas are written to rather than a file, say so in " +
                    "ContentLoader.VocabularySchemas."));
            }
            else if (used.Contains(schema) && awaitingContent)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanSchema, schema,
                    "is listed in ContentLoader.SchemasAwaitingContent but now governs a data file. " +
                    "The exemption has outlived its milestone — remove it."));
            }
            else if (used.Contains(schema) && vocabulary)
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.OrphanSchema, schema,
                    "is listed in ContentLoader.VocabularySchemas — a shape other schemas are written " +
                    "to, never a file's governor — but it now governs a data file. Either the file is " +
                    "misnamed, or the schema has become a content type and belongs out of that list."));
            }
        }

        return pairing;
    }

    private static Dictionary<string, IReadOnlyList<PatternBinding>> ValidateAgainstSchemas(
        IReadOnlyDictionary<string, ContentValue> data,
        IReadOnlyDictionary<string, ContentValue> schemas,
        IReadOnlyDictionary<string, string> pairing,
        IReadOnlySet<string> unusableSchemas,
        List<ContentIssue> issues)
    {
        var bindings = new Dictionary<string, IReadOnlyList<PatternBinding>>(StringComparer.Ordinal);

        foreach (var (path, schemaPath) in pairing
                     .Where(p => !unusableSchemas.Contains(p.Value))
                     .OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            issues.AddRange(JsonSchemaValidator.Validate(data[path], schemas[schemaPath], path, out var found));
            bindings[path] = found;
        }

        return bindings;
    }

}
