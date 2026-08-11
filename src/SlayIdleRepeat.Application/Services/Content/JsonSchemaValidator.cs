using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// One place where a schema <c>pattern</c> governed a string value — the trace
/// <see cref="ContentInvariants"/> uses to tell a declared id from a reference to one.
/// </summary>
/// <param name="Pattern">The regular expression the schema applied.</param>
/// <param name="MemberName">The member the value sat under, or the array index.</param>
/// <param name="Value">The string value.</param>
/// <param name="Location">Document path plus pointer.</param>
public sealed record PatternBinding(string Pattern, string MemberName, string Value, string Location);

/// <summary>
/// Validates a loaded document against its JSON Schema (draft 2020-12), over a deliberately
/// closed keyword set.
/// </summary>
/// <remarks>
/// <para>
/// Hand-written because it has to be: `23` §2.1 — architecture-tested — forbids
/// <c>SlayIdleRepeat.Application</c> any <c>PackageReference</c> at all, so no third-party
/// JSON-Schema library is available to the layer that owns validation.
/// </para>
/// <para>
/// 🔒 <b>Unknown keyword ⇒ hard failure, and the check is eager.</b>
/// <see cref="CheckSchemaKeywords"/> sweeps the whole schema document before any instance is
/// validated, so a keyword in a branch no data file happens to reach still fails the build. An
/// instance-driven check would only ever meet the keywords today's 37 documents walk into: adding
/// <c>allOf</c> to an unexercised <c>properties</c> entry, an empty array's <c>items</c>, or an
/// unmatched <c>oneOf</c> branch would pass silently — which is exactly
/// `SlayIdleRepeat.Data/README.md`'s <em>"a permissive schema is worse than no schema, because it
/// manufactures confidence."</em>
/// </para>
/// <para>
/// The same principle governs the assertion keywords: where a keyword's <em>value</em> has a shape
/// this validator cannot act on (<c>"uniqueItems": "true"</c>, <c>"multipleOf": 0</c>, an
/// <c>additionalProperties</c> that is neither a boolean nor a schema), it reports
/// <see cref="ContentIssueCode.UnsupportedSchemaKeyword"/> rather than skipping the check. Silently
/// skipping is how a bound stops biting without anybody noticing.
/// </para>
/// </remarks>
public static class JsonSchemaValidator
{
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    /// <summary>
    /// How deep <c>$ref</c> resolution may go before the validator concludes the schema graph is
    /// cyclic. A self-referential <c>$ref</c> would otherwise be a <see cref="StackOverflowException"/>,
    /// which cannot be caught: the process would die with no message and no document named.
    /// </summary>
    private const int MaxEvaluationDepth = 128;

    /// <summary>
    /// A ceiling on regular-expression matching. The patterns come out of data files, and a pattern
    /// with catastrophic backtracking would otherwise hang CI rather than fail it.
    /// </summary>
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromSeconds(2);

    /// <summary>The keyword positions whose <em>member values</em> are themselves schemas.</summary>
    private static readonly string[] SchemaMapKeywords = ["properties", "patternProperties", "$defs"];

    /// <summary>The keyword positions whose <em>value</em> is a single schema.</summary>
    private static readonly string[] SchemaKeywords = ["items", "propertyNames", "additionalProperties"];

    /// <summary>The keyword positions whose value is an <em>array</em> of schemas.</summary>
    private static readonly string[] SchemaListKeywords = ["oneOf"];

    /// <summary>
    /// Every keyword this validator understands. Annotations are accepted and ignored; assertions
    /// are enforced. Extending this set is a deliberate act with a test attached.
    /// </summary>
    public static IReadOnlySet<string> SupportedKeywords { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        // Annotations — read by humans and by the 📐 audit, never asserted.
        "$schema", "$id", "$comment", "title", "description", "default", "examples", "deprecated",

        // Structure.
        "$defs", "$ref",

        // Applicators.
        "properties", "patternProperties", "additionalProperties", "propertyNames", "items", "oneOf",

        // Assertions.
        "type", "enum", "const", "required",
        "minProperties", "maxProperties",
        "minItems", "maxItems", "uniqueItems",
        "minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum", "multipleOf",
        "minLength", "maxLength", "pattern", "format",
    };

    /// <summary>
    /// 🔒 Sweeps a whole schema document for keywords this validator does not implement, before any
    /// instance is validated against it. Run once per schema, not once per data file.
    /// </summary>
    public static IReadOnlyList<ContentIssue> CheckSchemaKeywords(ContentValue schema, string schemaPath)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var issues = new List<ContentIssue>();
        Sweep(schema, schemaPath, string.Empty, issues);
        return issues;
    }

    /// <summary>Validates one instance document against one schema document.</summary>
    /// <param name="instance">The parsed data document.</param>
    /// <param name="schema">The parsed schema document, which is also the <c>$ref</c> root.</param>
    /// <param name="instanceLocation">Document path used to locate findings.</param>
    public static IReadOnlyList<ContentIssue> Validate(
        ContentValue instance, ContentValue schema, string instanceLocation) =>
        Validate(instance, schema, instanceLocation, out _);

    /// <summary>
    /// Validates, and also reports every place a <c>pattern</c> governed a string — the trace the
    /// cross-file reference rules need.
    /// </summary>
    public static IReadOnlyList<ContentIssue> Validate(
        ContentValue instance,
        ContentValue schema,
        string instanceLocation,
        out IReadOnlyList<PatternBinding> patternBindings)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(schema);

        var run = new Run(schema, instanceLocation);
        run.Check(instance, schema, string.Empty, "(root)", 0);
        patternBindings = run.Bindings;
        return run.Issues;
    }

    /// <summary>
    /// Walks the schema's own grammar rather than guessing from pointer text: the members of
    /// <c>properties</c>, <c>patternProperties</c> and <c>$defs</c> are names the author chose, and
    /// everything else in a schema object is a keyword.
    /// </summary>
    private static void Sweep(ContentValue node, string schemaPath, string pointer, List<ContentIssue> issues)
    {
        if (node.Kind != ContentValueKind.Object)
        {
            return;
        }

        foreach (var name in node.MemberNames)
        {
            node.TryGetMember(name, out var value);
            var childPointer = $"{pointer}/{name}";

            if (!SupportedKeywords.Contains(name))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.UnsupportedSchemaKeyword, $"{schemaPath}#{childPointer}",
                    $"the schema uses the keyword '{name}', which this validator does not implement. " +
                    "A validator that ignored it would manufacture confidence — add it to " +
                    "JsonSchemaValidator.SupportedKeywords with a negative test, or remove it."));
                continue;
            }

            if (SchemaMapKeywords.Contains(name, StringComparer.Ordinal))
            {
                if (value!.Kind != ContentValueKind.Object)
                {
                    continue;
                }

                foreach (var member in value.MemberNames)
                {
                    value.TryGetMember(member, out var subschema);
                    Sweep(subschema!, schemaPath, $"{childPointer}/{member}", issues);
                }

                continue;
            }

            if (SchemaKeywords.Contains(name, StringComparer.Ordinal))
            {
                Sweep(value!, schemaPath, childPointer, issues);
                continue;
            }

            if (!SchemaListKeywords.Contains(name, StringComparer.Ordinal))
            {
                // enum, required, type, const, examples, default — values, not schemas.
                continue;
            }

            for (var i = 0; i < value!.Items.Count; i++)
            {
                Sweep(value.Items[i], schemaPath, $"{childPointer}/{i}", issues);
            }
        }
    }

    /// <remarks>
    /// 🔒 <c>\z</c> rather than <c>$</c>: .NET's <c>$</c> also matches immediately before a trailing
    /// newline, ECMA-262's (which draft 2020-12 specifies) does not. Without this,
    /// <c>"^AD_[A-Z0-9_]+$"</c> accepts <c>"AD_ENERGY\n"</c> and an id with a newline in it flows
    /// into the snapshot and into the cross-file id spaces.
    /// </remarks>
    private static Regex Compiled(string pattern) =>
        Patterns.GetOrAdd(pattern, p => new Regex(EndAnchored(p), RegexOptions.CultureInvariant, PatternTimeout));

    private static string EndAnchored(string pattern) =>
        pattern.Length > 1 && pattern.EndsWith('$') && !pattern.EndsWith(@"\$", StringComparison.Ordinal)
            ? pattern[..^1] + @"\z"
            : pattern;

    private sealed class Run(ContentValue root, string documentPath)
    {
        internal List<ContentIssue> Issues { get; } = [];

        internal List<PatternBinding> Bindings { get; } = [];

        internal void Check(ContentValue instance, ContentValue schema, string pointer, string memberName, int depth)
        {
            if (depth > MaxEvaluationDepth)
            {
                Add(ContentIssueCode.UnsupportedSchemaKeyword, pointer,
                    $"schema evaluation passed {MaxEvaluationDepth} levels. A $ref almost certainly " +
                    "points at itself; this validator does not implement recursive schemas.");
                return;
            }

            if (schema.Kind != ContentValueKind.Object)
            {
                Add(ContentIssueCode.UnsupportedSchemaKeyword, pointer,
                    "the schema at this position is not an object. Boolean schemas are legal JSON " +
                    "Schema but this validator does not implement them — extend JsonSchemaValidator.");
                return;
            }

            if (schema.TryGetMember("$ref", out var reference))
            {
                var target = Resolve(reference!, pointer);
                if (target is not null)
                {
                    Check(instance, target, pointer, memberName, depth + 1);
                }
            }

            CheckType(instance, schema, pointer);
            CheckEnumAndConst(instance, schema, pointer);
            CheckNumber(instance, schema, pointer);
            CheckText(instance, schema, pointer, memberName);
            CheckArray(instance, schema, pointer, depth);
            CheckObject(instance, schema, pointer, depth);
            CheckOneOf(instance, schema, pointer, memberName, depth);
        }

        private void CheckType(ContentValue instance, ContentValue schema, string pointer)
        {
            if (!schema.TryGetMember("type", out var type))
            {
                return;
            }

            // Hot path: one allocation-free pass over the permitted names, and the message — which
            // needs a joined string — is only built when the value is actually wrong.
            if (type!.Kind == ContentValueKind.Array)
            {
                foreach (var candidate in type.Items)
                {
                    if (Matches(instance, candidate.AsText(SchemaKeyword(pointer, "type"))))
                    {
                        return;
                    }
                }
            }
            else if (Matches(instance, type.AsText(SchemaKeyword(pointer, "type"))))
            {
                return;
            }

            var allowed = type.Kind == ContentValueKind.Array
                ? string.Join(", ", type.Items.Select(i => i.AsText()))
                : type.AsText();

            Add(ContentIssueCode.SchemaViolation, pointer,
                instance.IsUnauthorised
                    ? $"is null, but the schema permits only [{allowed}]. A null here would mean " +
                      "'unauthorised', and this key is not allowed to be unauthorised."
                    : $"is {instance.Kind}, but the schema permits only [{allowed}].");
        }

        private static bool Matches(ContentValue instance, string typeName) => typeName switch
        {
            "object" => instance.Kind == ContentValueKind.Object,
            "array" => instance.Kind == ContentValueKind.Array,
            "string" => instance.Kind == ContentValueKind.Text,
            "boolean" => instance.Kind == ContentValueKind.Boolean,
            "null" => instance.IsUnauthorised,
            "number" => instance.Kind == ContentValueKind.Number,
            "integer" => instance.Kind == ContentValueKind.Number &&
                         decimal.Truncate(instance.AsNumber()) == instance.AsNumber(),
            _ => false,
        };

        private void CheckEnumAndConst(ContentValue instance, ContentValue schema, string pointer)
        {
            if (schema.TryGetMember("enum", out var permitted) &&
                !permitted!.Items.Any(candidate => candidate.Equals(instance)))
            {
                Add(Classify(instance), pointer,
                    $"holds {instance} which is not one of the {permitted.Items.Count} values the " +
                    "schema enumerates.");
            }

            if (schema.TryGetMember("const", out var constant) && !constant!.Equals(instance))
            {
                Add(Classify(instance), pointer,
                    $"holds {instance} but the schema locks this key to {constant}. A const in these " +
                    "schemas encodes a design rule the docs locked — changing it is a design change.");
            }
        }

        private void CheckNumber(ContentValue instance, ContentValue schema, string pointer)
        {
            if (instance.Kind != ContentValueKind.Number)
            {
                return;
            }

            var value = instance.AsNumber();

            if (schema.TryGetMember("minimum", out var minimum) &&
                value < minimum!.AsNumber(SchemaKeyword(pointer, "minimum")))
            {
                Add(ContentIssueCode.OutOfRange, pointer, $"is {value}, below the minimum {minimum.AsNumber()}.");
            }

            if (schema.TryGetMember("maximum", out var maximum) &&
                value > maximum!.AsNumber(SchemaKeyword(pointer, "maximum")))
            {
                Add(ContentIssueCode.OutOfRange, pointer, $"is {value}, above the maximum {maximum.AsNumber()}.");
            }

            if (schema.TryGetMember("exclusiveMinimum", out var exclusiveMinimum) &&
                value <= exclusiveMinimum!.AsNumber(SchemaKeyword(pointer, "exclusiveMinimum")))
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"is {value}, which is not above the exclusive minimum {exclusiveMinimum.AsNumber()}.");
            }

            if (schema.TryGetMember("exclusiveMaximum", out var exclusiveMaximum) &&
                value >= exclusiveMaximum!.AsNumber(SchemaKeyword(pointer, "exclusiveMaximum")))
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"is {value}, which is not below the exclusive maximum {exclusiveMaximum.AsNumber()}.");
            }

            if (!schema.TryGetMember("multipleOf", out var multipleOf))
            {
                return;
            }

            var divisor = multipleOf!.AsNumber(SchemaKeyword(pointer, "multipleOf"));
            if (divisor <= 0m)
            {
                // Skipping here would be a bound that silently stopped biting.
                Add(ContentIssueCode.UnsupportedSchemaKeyword, pointer,
                    $"multipleOf is {divisor}; draft 2020-12 requires it to be greater than zero.");
            }
            else if (value % divisor != 0m)
            {
                Add(ContentIssueCode.OutOfRange, pointer, $"is {value}, which is not a multiple of {divisor}.");
            }
        }

        private void CheckText(ContentValue instance, ContentValue schema, string pointer, string memberName)
        {
            if (instance.Kind != ContentValueKind.Text)
            {
                return;
            }

            var value = instance.AsText();

            // Draft 2020-12 counts code points; this counts UTF-16 code units, so a non-BMP
            // character counts twice. No length-constrained string in the set contains one, and the
            // difference can only ever make this validator STRICTER, never more permissive.
            if (schema.TryGetMember("minLength", out var minLength) &&
                value.Length < minLength!.AsInt32(SchemaKeyword(pointer, "minLength")))
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"is {value.Length} characters long, below the minimum {minLength.AsInt32()}.");
            }

            if (schema.TryGetMember("maxLength", out var maxLength) &&
                value.Length > maxLength!.AsInt32(SchemaKeyword(pointer, "maxLength")))
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"is {value.Length} characters long, above the maximum {maxLength.AsInt32()}.");
            }

            if (schema.TryGetMember("pattern", out var pattern))
            {
                var expression = pattern!.AsText(SchemaKeyword(pointer, "pattern"));
                Bindings.Add(new PatternBinding(expression, memberName, value, Locate(pointer)));

                if (!Compiled(expression).IsMatch(value))
                {
                    Add(ContentIssueCode.UnknownId, pointer,
                        $"holds '{value}', which does not match the id pattern {expression}.");
                }
            }

            if (schema.TryGetMember("format", out var format))
            {
                CheckFormat(value, format!.AsText(SchemaKeyword(pointer, "format")), pointer);
            }
        }

        private void CheckFormat(string value, string format, string pointer)
        {
            if (!string.Equals(format, "date-time", StringComparison.Ordinal))
            {
                Add(ContentIssueCode.UnsupportedSchemaKeyword, pointer,
                    $"the schema asks for format '{format}', which this validator cannot check. " +
                    "An unchecked format is an assertion nobody is making — implement it in " +
                    "JsonSchemaValidator.CheckFormat or drop the keyword.");
                return;
            }

            if (!DateTimeOffset.TryParse(
                    value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
            {
                Add(ContentIssueCode.SchemaViolation, pointer, $"'{value}' is not an RFC 3339 date-time.");
            }
        }

        private void CheckArray(ContentValue instance, ContentValue schema, string pointer, int depth)
        {
            if (instance.Kind != ContentValueKind.Array)
            {
                return;
            }

            if (schema.TryGetMember("minItems", out var minItems) &&
                instance.Items.Count < minItems!.AsInt32(SchemaKeyword(pointer, "minItems")))
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"holds {instance.Items.Count} item(s), below the minimum {minItems.AsInt32()}.");
            }

            if (schema.TryGetMember("maxItems", out var maxItems) &&
                instance.Items.Count > maxItems!.AsInt32(SchemaKeyword(pointer, "maxItems")))
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"holds {instance.Items.Count} item(s), above the maximum {maxItems.AsInt32()}.");
            }

            CheckUniqueItems(instance, schema, pointer);

            if (!schema.TryGetMember("items", out var items))
            {
                return;
            }

            for (var i = 0; i < instance.Items.Count; i++)
            {
                Check(instance.Items[i], items!, $"{pointer}/{i}",
                    i.ToString(CultureInfo.InvariantCulture), depth + 1);
            }
        }

        private void CheckUniqueItems(ContentValue instance, ContentValue schema, string pointer)
        {
            if (!schema.TryGetMember("uniqueItems", out var unique))
            {
                return;
            }

            if (unique!.Kind != ContentValueKind.Boolean)
            {
                Add(ContentIssueCode.UnsupportedSchemaKeyword, pointer,
                    $"uniqueItems is {unique.Kind}; draft 2020-12 requires a boolean, and this " +
                    "validator will not guess at another shape.");
                return;
            }

            if (!unique.AsBoolean())
            {
                return;
            }

            for (var i = 0; i < instance.Items.Count; i++)
            {
                for (var j = i + 1; j < instance.Items.Count; j++)
                {
                    if (instance.Items[i].Equals(instance.Items[j]))
                    {
                        Add(ContentIssueCode.DuplicateId, $"{pointer}/{j}",
                            $"is identical to item {i}. 14 §6 fails the build on duplicate ids, " +
                            "and a duplicated collection entry is one wearing a different index.");
                    }
                }
            }
        }

        private void CheckObject(ContentValue instance, ContentValue schema, string pointer, int depth)
        {
            if (instance.Kind != ContentValueKind.Object)
            {
                return;
            }

            if (schema.TryGetMember("required", out var required))
            {
                foreach (var item in required!.Items)
                {
                    var name = item.AsText(SchemaKeyword(pointer, "required"));
                    if (!instance.TryGetMember(name, out _))
                    {
                        Add(ContentIssueCode.SchemaViolation, $"{pointer}/{Escape(name)}",
                            "is required by the schema and is absent.");
                    }
                }
            }

            if (schema.TryGetMember("minProperties", out var minProperties) &&
                instance.MemberNames.Count < minProperties!.AsInt32(SchemaKeyword(pointer, "minProperties")))
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"holds {instance.MemberNames.Count} propert(ies), below the minimum {minProperties.AsInt32()}.");
            }

            if (schema.TryGetMember("maxProperties", out var maxProperties) &&
                instance.MemberNames.Count > maxProperties!.AsInt32(SchemaKeyword(pointer, "maxProperties")))
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"holds {instance.MemberNames.Count} propert(ies), above the maximum {maxProperties.AsInt32()}.");
            }

            schema.TryGetMember("properties", out var properties);
            schema.TryGetMember("patternProperties", out var patternProperties);
            schema.TryGetMember("additionalProperties", out var additionalProperties);
            schema.TryGetMember("propertyNames", out var propertyNames);

            foreach (var name in instance.MemberNames)
            {
                instance.TryGetMember(name, out var value);
                var childPointer = $"{pointer}/{Escape(name)}";
                var covered = false;

                if (propertyNames is not null)
                {
                    CheckPropertyName(name, propertyNames, childPointer, depth);
                }

                if (properties is not null && properties.TryGetMember(name, out var propertySchema))
                {
                    Check(value!, propertySchema!, childPointer, name, depth + 1);
                    covered = true;
                }

                if (patternProperties is not null)
                {
                    foreach (var expression in patternProperties.MemberNames)
                    {
                        if (Compiled(expression).IsMatch(name))
                        {
                            patternProperties.TryGetMember(expression, out var patternSchema);
                            Check(value!, patternSchema!, childPointer, name, depth + 1);
                            covered = true;
                        }
                    }
                }

                if (covered || additionalProperties is null)
                {
                    continue;
                }

                if (additionalProperties.Kind == ContentValueKind.Boolean)
                {
                    if (!additionalProperties.AsBoolean())
                    {
                        Add(ContentIssueCode.UnknownId, childPointer,
                            $"'{name}' is not declared by the schema, which sets additionalProperties: " +
                            "false. A key the schema does not know is either a typo or a tunable " +
                            "nobody documented.");
                    }
                }
                else if (additionalProperties.Kind == ContentValueKind.Object)
                {
                    Check(value!, additionalProperties, childPointer, name, depth + 1);
                }
                else
                {
                    Add(ContentIssueCode.UnsupportedSchemaKeyword, childPointer,
                        $"additionalProperties is {additionalProperties.Kind}; only a boolean or a " +
                        "schema object is implemented.");
                }
            }
        }

        private void CheckPropertyName(string name, ContentValue propertyNames, string childPointer, int depth)
        {
            var before = Issues.Count;
            Check(ContentValue.Text(name), propertyNames, childPointer, name, depth + 1);

            // A name that breaks the convention is an unknown id, not a value fault — but a keyword
            // the validator cannot honour stays what it is.
            for (var i = before; i < Issues.Count; i++)
            {
                if (Issues[i].Code != ContentIssueCode.UnsupportedSchemaKeyword)
                {
                    Issues[i] = Issues[i] with { Code = ContentIssueCode.UnknownId };
                }
            }
        }

        private void CheckOneOf(
            ContentValue instance, ContentValue schema, string pointer, string memberName, int depth)
        {
            if (!schema.TryGetMember("oneOf", out var branches))
            {
                return;
            }

            var matched = 0;
            Run? closest = null;

            foreach (var branch in branches!.Items)
            {
                // A branch whose declared type cannot admit this instance never needs a full walk.
                if (branch.Kind == ContentValueKind.Object &&
                    branch.TryGetMember("type", out var branchType) &&
                    !Admits(branchType!, instance))
                {
                    continue;
                }

                var probe = new Run(root, documentPath);
                probe.Check(instance, branch, pointer, memberName, depth + 1);

                if (probe.Issues.Count == 0)
                {
                    matched++;
                    Bindings.AddRange(probe.Bindings);
                }
                else if (closest is null || probe.Issues.Count < closest.Issues.Count)
                {
                    closest = probe;
                }
            }

            if (matched == 1)
            {
                return;
            }

            // A oneOf that matched nothing otherwise reports only "0 of 2 branches", which tells a
            // reader nothing about which value is wrong.
            Add(ContentIssueCode.SchemaViolation, pointer,
                $"matches {matched} of the {branches.Items.Count} oneOf branches; exactly one must match." +
                (matched == 0 && closest is not null
                    ? $" The closest branch failed with: {string.Join("; ", closest.Issues.Select(i => i.Message))}"
                    : string.Empty));
        }

        private static bool Admits(ContentValue type, ContentValue instance) =>
            type.Kind == ContentValueKind.Array
                ? type.Items.Any(candidate => candidate.Kind == ContentValueKind.Text &&
                                              Matches(instance, candidate.AsText()))
                : type.Kind != ContentValueKind.Text || Matches(instance, type.AsText());

        private ContentValue? Resolve(ContentValue reference, string pointer)
        {
            var target = reference.AsText(SchemaKeyword(pointer, "$ref"));
            if (!target.StartsWith("#/", StringComparison.Ordinal))
            {
                Add(ContentIssueCode.UnsupportedSchemaKeyword, pointer,
                    $"the schema uses the external reference '{target}'. This validator resolves " +
                    "only same-document pointers; a cross-file $ref would make the schema set a " +
                    "graph nobody can review file by file.");
                return null;
            }

            var current = root;
            foreach (var raw in target[2..].Split('/'))
            {
                var segment = raw
                    .Replace("~1", "/", StringComparison.Ordinal)
                    .Replace("~0", "~", StringComparison.Ordinal);

                if (!current.TryGetMember(segment, out var next))
                {
                    Add(ContentIssueCode.SchemaViolation, pointer,
                        $"the schema reference '{target}' resolves to nothing.");
                    return null;
                }

                current = next!;
            }

            return current;
        }

        private static ContentIssueCode Classify(ContentValue instance) =>
            instance.Kind == ContentValueKind.Text ? ContentIssueCode.UnknownId : ContentIssueCode.SchemaViolation;

        /// <summary>RFC 6901: a member name in a pointer escapes <c>~</c> and <c>/</c>.</summary>
        private static string Escape(string memberName) =>
            memberName
                .Replace("~", "~0", StringComparison.Ordinal)
                .Replace("/", "~1", StringComparison.Ordinal);

        /// <summary>
        /// Names a schema keyword's own value in a failure message. Without it, a malformed schema
        /// (<c>"minimum": "5"</c>) surfaces as a bare type-mismatch naming neither file nor pointer.
        /// </summary>
        private string SchemaKeyword(string pointer, string keyword) =>
            $"{Locate(pointer)} (schema keyword '{keyword}')";

        private void Add(ContentIssueCode code, string pointer, string message) =>
            Issues.Add(new ContentIssue(code, Locate(pointer), message));

        private string Locate(string pointer) =>
            pointer.Length == 0 ? documentPath : $"{documentPath}#{pointer}";
    }
}
