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
/// 🔒 <b>Unknown keyword ⇒ hard failure.</b> The usual danger with a hand-written validator is
/// that it quietly ignores what it does not implement, which is precisely
/// `SlayIdleRepeat.Data/README.md`'s <em>"a permissive schema is worse than no schema, because it
/// manufactures confidence."</em> Every keyword this validator meets must be on
/// <see cref="SupportedKeywords"/>; anything else is
/// <see cref="ContentIssueCode.UnsupportedSchemaKeyword"/> and fails the build with an
/// instruction to extend this class. That turns "silently permissive" into "loudly incomplete".
/// </para>
/// </remarks>
public static class JsonSchemaValidator
{
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

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
        var run = new Run(schema, instanceLocation);
        run.Check(instance, schema, string.Empty, "(root)");
        patternBindings = run.Bindings;
        return run.Issues;
    }

    private static Regex Compiled(string pattern) =>
        Patterns.GetOrAdd(pattern, p => new Regex(p, RegexOptions.CultureInvariant));

    private sealed class Run(ContentValue root, string documentPath)
    {
        internal List<ContentIssue> Issues { get; } = [];

        internal List<PatternBinding> Bindings { get; } = [];

        internal void Check(ContentValue instance, ContentValue schema, string pointer, string memberName)
        {
            if (schema.Kind != ContentValueKind.Object)
            {
                Add(ContentIssueCode.UnsupportedSchemaKeyword, pointer,
                    "the schema at this position is not an object. Boolean schemas are legal JSON " +
                    "Schema but this validator does not implement them — extend JsonSchemaValidator.");
                return;
            }

            foreach (var keyword in schema.MemberNames)
            {
                if (!SupportedKeywords.Contains(keyword))
                {
                    Add(ContentIssueCode.UnsupportedSchemaKeyword, pointer,
                        $"the schema uses the keyword '{keyword}', which this validator does not " +
                        "implement. A validator that ignored it would manufacture confidence — " +
                        "add it to JsonSchemaValidator.SupportedKeywords with a test, or remove it.");
                }
            }

            if (schema.TryGetMember("$ref", out var reference))
            {
                var target = Resolve(reference!, pointer);
                if (target is not null)
                {
                    Check(instance, target, pointer, memberName);
                }
            }

            CheckType(instance, schema, pointer);
            CheckEnumAndConst(instance, schema, pointer);
            CheckNumber(instance, schema, pointer);
            CheckText(instance, schema, pointer, memberName);
            CheckArray(instance, schema, pointer);
            CheckObject(instance, schema, pointer);
            CheckOneOf(instance, schema, pointer, memberName);
        }

        private void CheckType(ContentValue instance, ContentValue schema, string pointer)
        {
            if (!schema.TryGetMember("type", out var type))
            {
                return;
            }

            var allowed = type!.Kind == ContentValueKind.Array
                ? type.Items.Select(i => i.AsText()).ToArray()
                : [type.AsText()];

            if (allowed.Any(name => Matches(instance, name)))
            {
                return;
            }

            Add(ContentIssueCode.SchemaViolation, pointer,
                instance.IsUnauthorised
                    ? $"is null, but the schema permits only [{string.Join(", ", allowed)}]. A null " +
                      "here would mean 'unauthorised', and this key is not allowed to be unauthorised."
                    : $"is {instance.Kind}, but the schema permits only [{string.Join(", ", allowed)}].");
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

            if (schema.TryGetMember("minimum", out var minimum) && value < minimum!.AsNumber())
            {
                Add(ContentIssueCode.OutOfRange, pointer, $"is {value}, below the minimum {minimum.AsNumber()}.");
            }

            if (schema.TryGetMember("maximum", out var maximum) && value > maximum!.AsNumber())
            {
                Add(ContentIssueCode.OutOfRange, pointer, $"is {value}, above the maximum {maximum.AsNumber()}.");
            }

            if (schema.TryGetMember("exclusiveMinimum", out var exclusiveMinimum) &&
                value <= exclusiveMinimum!.AsNumber())
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"is {value}, which is not above the exclusive minimum {exclusiveMinimum.AsNumber()}.");
            }

            if (schema.TryGetMember("exclusiveMaximum", out var exclusiveMaximum) &&
                value >= exclusiveMaximum!.AsNumber())
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"is {value}, which is not below the exclusive maximum {exclusiveMaximum.AsNumber()}.");
            }

            if (schema.TryGetMember("multipleOf", out var multipleOf) &&
                multipleOf!.AsNumber() != 0m && value % multipleOf.AsNumber() != 0m)
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"is {value}, which is not a multiple of {multipleOf.AsNumber()}.");
            }
        }

        private void CheckText(ContentValue instance, ContentValue schema, string pointer, string memberName)
        {
            if (instance.Kind != ContentValueKind.Text)
            {
                return;
            }

            var value = instance.AsText();

            if (schema.TryGetMember("minLength", out var minLength) && value.Length < minLength!.AsInt32())
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"is {value.Length} characters long, below the minimum {minLength.AsInt32()}.");
            }

            if (schema.TryGetMember("maxLength", out var maxLength) && value.Length > maxLength!.AsInt32())
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"is {value.Length} characters long, above the maximum {maxLength.AsInt32()}.");
            }

            if (schema.TryGetMember("pattern", out var pattern))
            {
                var expression = pattern!.AsText();
                Bindings.Add(new PatternBinding(expression, memberName, value, Locate(pointer)));

                if (!Compiled(expression).IsMatch(value))
                {
                    Add(ContentIssueCode.UnknownId, pointer,
                        $"holds '{value}', which does not match the id pattern {expression}.");
                }
            }

            if (schema.TryGetMember("format", out var format))
            {
                CheckFormat(value, format!.AsText(), pointer);
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

        private void CheckArray(ContentValue instance, ContentValue schema, string pointer)
        {
            if (instance.Kind != ContentValueKind.Array)
            {
                return;
            }

            if (schema.TryGetMember("minItems", out var minItems) && instance.Items.Count < minItems!.AsInt32())
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"holds {instance.Items.Count} item(s), below the minimum {minItems.AsInt32()}.");
            }

            if (schema.TryGetMember("maxItems", out var maxItems) && instance.Items.Count > maxItems!.AsInt32())
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"holds {instance.Items.Count} item(s), above the maximum {maxItems.AsInt32()}.");
            }

            if (schema.TryGetMember("uniqueItems", out var unique) &&
                unique!.Kind == ContentValueKind.Boolean && unique.AsBoolean())
            {
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

            if (schema.TryGetMember("items", out var items))
            {
                for (var i = 0; i < instance.Items.Count; i++)
                {
                    Check(instance.Items[i], items!, $"{pointer}/{i}", i.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        private void CheckObject(ContentValue instance, ContentValue schema, string pointer)
        {
            if (instance.Kind != ContentValueKind.Object)
            {
                return;
            }

            if (schema.TryGetMember("required", out var required))
            {
                foreach (var name in required!.Items.Select(i => i.AsText()))
                {
                    if (!instance.TryGetMember(name, out _))
                    {
                        Add(ContentIssueCode.SchemaViolation, $"{pointer}/{name}",
                            "is required by the schema and is absent.");
                    }
                }
            }

            if (schema.TryGetMember("minProperties", out var minProperties) &&
                instance.MemberNames.Count < minProperties!.AsInt32())
            {
                Add(ContentIssueCode.OutOfRange, pointer,
                    $"holds {instance.MemberNames.Count} propert(ies), below the minimum {minProperties.AsInt32()}.");
            }

            if (schema.TryGetMember("maxProperties", out var maxProperties) &&
                instance.MemberNames.Count > maxProperties!.AsInt32())
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
                var childPointer = $"{pointer}/{name}";
                var covered = false;

                if (propertyNames is not null)
                {
                    var before = Issues.Count;
                    Check(ContentValue.Text(name), propertyNames, childPointer, name);
                    if (Issues.Count > before)
                    {
                        // A name that breaks the convention is an unknown id, not a value fault.
                        for (var i = before; i < Issues.Count; i++)
                        {
                            if (Issues[i].Code != ContentIssueCode.UnsupportedSchemaKeyword)
                            {
                                Issues[i] = Issues[i] with { Code = ContentIssueCode.UnknownId };
                            }
                        }
                    }
                }

                if (properties is not null && properties.TryGetMember(name, out var propertySchema))
                {
                    Check(value!, propertySchema!, childPointer, name);
                    covered = true;
                }

                if (patternProperties is not null)
                {
                    foreach (var expression in patternProperties.MemberNames)
                    {
                        if (Compiled(expression).IsMatch(name))
                        {
                            patternProperties.TryGetMember(expression, out var patternSchema);
                            Check(value!, patternSchema!, childPointer, name);
                            covered = true;
                        }
                    }
                }

                if (covered || additionalProperties is null)
                {
                    continue;
                }

                if (additionalProperties.Kind == ContentValueKind.Boolean && !additionalProperties.AsBoolean())
                {
                    Add(ContentIssueCode.UnknownId, childPointer,
                        $"'{name}' is not declared by the schema, which sets additionalProperties: false. " +
                        "A key the schema does not know is either a typo or a tunable nobody documented.");
                }
                else if (additionalProperties.Kind == ContentValueKind.Object)
                {
                    Check(value!, additionalProperties, childPointer, name);
                }
            }
        }

        private void CheckOneOf(ContentValue instance, ContentValue schema, string pointer, string memberName)
        {
            if (!schema.TryGetMember("oneOf", out var branches))
            {
                return;
            }

            var matched = 0;
            foreach (var branch in branches!.Items)
            {
                var probe = new Run(root, documentPath);
                probe.Check(instance, branch, pointer, memberName);
                if (probe.Issues.Count == 0)
                {
                    matched++;
                    Bindings.AddRange(probe.Bindings);
                }
            }

            if (matched != 1)
            {
                Add(ContentIssueCode.SchemaViolation, pointer,
                    $"matches {matched} of the {branches.Items.Count} oneOf branches; exactly one must match.");
            }
        }

        private ContentValue? Resolve(ContentValue reference, string pointer)
        {
            var target = reference.AsText();
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

        private void Add(ContentIssueCode code, string pointer, string message) =>
            Issues.Add(new ContentIssue(code, Locate(pointer), message));

        private string Locate(string pointer) =>
            pointer.Length == 0 ? documentPath : $"{documentPath}#{pointer}";
    }
}
