using System.Text.Json;
using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Application.Services.Content;

/// <summary>
/// Strict UTF-8 JSON → <see cref="ContentValue"/>. The only place in the codebase that knows JSON
/// exists on the read path.
/// </summary>
/// <remarks>
/// Strict means strict: RFC 8259 only — no comments, no trailing commas — so CI can never be more
/// forgiving than the runtime loader. It also reports <see cref="ContentIssueCode.DuplicateKey"/>,
/// which a plain deserialise cannot: the object model keeps the last duplicate and the earlier
/// value simply disappears.
/// <para>
/// 🔒 JSON <c>null</c> becomes <see cref="ContentValue.Unauthorised"/>, never a default.
/// </para>
/// </remarks>
public static class JsonContentReader
{
    private static readonly JsonReaderOptions Strict = new()
    {
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 64,
    };

    /// <summary>Parses one document.</summary>
    /// <param name="documentPath">Used only to locate findings.</param>
    /// <param name="utf8">The raw bytes.</param>
    /// <param name="root">The parsed root, or null when <paramref name="issues"/> is non-empty.</param>
    /// <param name="issues">Everything wrong with the bytes.</param>
    public static bool TryRead(
        string documentPath,
        ReadOnlySpan<byte> utf8,
        out ContentValue? root,
        out IReadOnlyList<ContentIssue> issues)
    {
        var found = new List<ContentIssue>();
        root = null;

        if (utf8.Length == 0)
        {
            found.Add(new ContentIssue(
                ContentIssueCode.MalformedJson, documentPath,
                "The file is empty. An empty content file is never intentional."));
            issues = found;
            return false;
        }

        try
        {
            var reader = new Utf8JsonReader(utf8, Strict);
            if (!reader.Read())
            {
                found.Add(new ContentIssue(
                    ContentIssueCode.MalformedJson, documentPath, "The file holds no JSON value."));
                issues = found;
                return false;
            }

            var value = ReadValue(ref reader, documentPath, string.Empty, found);

            if (reader.Read())
            {
                found.Add(new ContentIssue(
                    ContentIssueCode.MalformedJson, documentPath,
                    "There is more than one JSON value in the file."));
            }

            if (found.Count == 0)
            {
                root = value;
            }
        }
        catch (JsonException exception)
        {
            found.Add(new ContentIssue(
                ContentIssueCode.MalformedJson, documentPath,
                $"does not parse as strict RFC 8259 JSON (no comments, no trailing commas) — " +
                exception.Message));
        }

        issues = found;
        return found.Count == 0;
    }

    private static ContentValue ReadValue(
        ref Utf8JsonReader reader, string documentPath, string pointer, List<ContentIssue> issues) =>
        reader.TokenType switch
        {
            JsonTokenType.StartObject => ReadObject(ref reader, documentPath, pointer, issues),
            JsonTokenType.StartArray => ReadArray(ref reader, documentPath, pointer, issues),
            JsonTokenType.String => ContentValue.Text(reader.GetString()!),
            JsonTokenType.True => ContentValue.True,
            JsonTokenType.False => ContentValue.False,

            // 🔒 The whole point. `game-data/README.md`: null means "the design docs do
            // not authorise a value here". It is never zero and never a default.
            JsonTokenType.Null => ContentValue.Unauthorised,

            JsonTokenType.Number => ReadNumber(ref reader, documentPath, pointer, issues),
            _ => throw new JsonException($"Unexpected token {reader.TokenType}."),
        };

    private static ContentValue ReadNumber(
        ref Utf8JsonReader reader, string documentPath, string pointer, List<ContentIssue> issues)
    {
        if (reader.TryGetDecimal(out var number))
        {
            return ContentValue.Number(number);
        }

        issues.Add(new ContentIssue(
            ContentIssueCode.MalformedJson, Locate(documentPath, pointer),
            "the number does not fit an exact decimal. Content numbers are held exactly so the " +
            "load path never rounds; a value needing binary floating point does not belong here."));

        // Never a zero, even as a placeholder the issue above already short-circuits: a literal 0
        // standing in for an unrepresentable value is the exact shape the null convention forbids.
        return ContentValue.Unauthorised;
    }

    private static ContentValue ReadObject(
        ref Utf8JsonReader reader, string documentPath, string pointer, List<ContentIssue> issues)
    {
        var members = new List<KeyValuePair<string, ContentValue>>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return ContentValue.Object(members);
            }

            var name = reader.GetString()!;
            if (!seen.Add(name))
            {
                issues.Add(new ContentIssue(
                    ContentIssueCode.DuplicateKey, Locate(documentPath, $"{pointer}/{name}"),
                    $"property '{name}' appears more than once. One of the values is silently " +
                    "discarded on load — 14 §6 forbids duplicate ids for exactly this reason."));
            }

            reader.Read();
            var value = ReadValue(ref reader, documentPath, $"{pointer}/{name}", issues);
            if (seen.Count == members.Count + 1)
            {
                members.Add(new KeyValuePair<string, ContentValue>(name, value));
            }
        }

        throw new JsonException("The object is not closed.");
    }

    private static ContentValue ReadArray(
        ref Utf8JsonReader reader, string documentPath, string pointer, List<ContentIssue> issues)
    {
        var items = new List<ContentValue>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                return ContentValue.Array(items);
            }

            items.Add(ReadValue(ref reader, documentPath, $"{pointer}/{items.Count}", issues));
        }

        throw new JsonException("The array is not closed.");
    }

    private static string Locate(string documentPath, string pointer) =>
        pointer.Length == 0 ? documentPath : $"{documentPath}#{pointer}";
}
