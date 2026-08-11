using System.Text.Json;
using SlayIdleRepeat.Core.Model.Snapshots;

namespace SlayIdleRepeat.Core.Tests.Model.Snapshots;

/// <summary>
/// The 🔒 field-order pin of `14` §16.6 — *"a CI test pins the field list per
/// <c>SchemaVersion</c>"* — and the comparison that makes it bite.
/// </summary>
/// <remarks>
/// <para>
/// The subject set is every public snapshot record in <c>SlayIdleRepeat.Core.Model.Snapshots</c>.
/// That set is <b>empty today</b>: <c>PlayerSnapshot</c> and <c>RunSnapshot</c> are <c>M1-04</c>
/// and <c>M1-05</c>'s. The rule is written against its final subject anyway, so it passes
/// vacuously now and turns into a real assertion the moment M1 declares the first record — with
/// no <c>Skip</c>, no placeholder and nobody having to remember to switch it on.
/// </para>
/// <para>
/// The pinned list is produced by <see cref="CanonicalStateWriter.CanonicalFieldOrder"/> — the
/// writer's <i>own</i> traversal — so the pin cannot drift from the bytes it is guarding.
/// </para>
/// </remarks>
internal static class SnapshotFieldOrderPin
{
    private const string ResourceName =
        "SlayIdleRepeat.Core.Tests.Model.Snapshots.SnapshotFieldOrder.json";

    private static readonly JsonDocument Document = Load();

    /// <summary>
    /// What a violation of this pin means, said plainly. It is the whole point of the rule: the
    /// answer to a failure here is a <c>SchemaVersion</c> bump and a migration, never a test edit.
    /// </summary>
    internal const string Consequence =
        "This is a SERIALISATION CHANGE (14 §16.6), not a test failure to fix in the test. " +
        "Adding, removing or reordering a snapshot field changes every stateHash in existence: " +
        "bump SnapshotSchema.SchemaVersion, add the pin for the new version, and handle it as a " +
        "versioned migration — never silently, and never by editing the pinned list for an " +
        "existing version.";

    /// <summary>Every public snapshot record in <c>Core/Model/Snapshots/</c>. Empty until M1.</summary>
    internal static IReadOnlyList<Type> SnapshotRecords { get; } =
        typeof(SnapshotSchema).Assembly
            .GetTypes()
            .Where(t => t.IsPublic && !t.IsNested)
            .Where(t => string.Equals(t.Namespace, "SlayIdleRepeat.Core.Model.Snapshots", StringComparison.Ordinal))
            .Where(CanonicalStateWriter.IsCanonicalRecord)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>Every schema version the pin file carries a section for.</summary>
    internal static IReadOnlyList<int> PinnedVersions { get; } =
        Document.RootElement.GetProperty("pins")
            .EnumerateArray()
            .Select(pin => pin.GetProperty("schemaVersion").GetInt32())
            .ToArray();

    /// <summary>The pinned field lists for a schema version, keyed by record simple name.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> For(int schemaVersion)
    {
        var pin = Document.RootElement.GetProperty("pins")
            .EnumerateArray()
            .SingleOrDefault(p => p.GetProperty("schemaVersion").GetInt32() == schemaVersion);

        if (pin.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException(
                $"SnapshotFieldOrder.json pins no field list for SchemaVersion {schemaVersion}. {Consequence}");
        }

        return pin.GetProperty("snapshots")
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => (IReadOnlyList<string>)property.Value
                    .EnumerateArray()
                    .Select(element => element.GetString()!)
                    .ToArray(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// Compares a record's actual canonical field order against its pinned one and describes
    /// every difference. Empty means the pin holds.
    /// </summary>
    /// <remarks>
    /// Factored out so the pin's own teeth are testable: the self-tests drive this with a
    /// deliberately wrong list, which proves an added, removed or reordered field is caught —
    /// without ever committing a violating record.
    /// </remarks>
    internal static IReadOnlyList<string> Violations(
        string recordName,
        int schemaVersion,
        IReadOnlyList<string> pinned,
        IReadOnlyList<string> actual)
    {
        var offenders = new List<string>();

        for (var i = 0; i < Math.Max(pinned.Count, actual.Count); i++)
        {
            var pinnedField = i < pinned.Count ? pinned[i] : null;
            var actualField = i < actual.Count ? actual[i] : null;

            if (string.Equals(pinnedField, actualField, StringComparison.Ordinal))
            {
                continue;
            }

            offenders.Add(
                $"{recordName} field {i} (SchemaVersion {schemaVersion}): " +
                $"pinned {Describe(pinnedField)}, found {Describe(actualField)}. {Consequence}");
        }

        return offenders;
    }

    private static string Describe(string? field) => field is null ? "<no field>" : $"'{field}'";

    private static JsonDocument Load()
    {
        using var stream = typeof(SnapshotFieldOrderPin).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The field-order pin '{ResourceName}' is not embedded in " +
                $"{typeof(SnapshotFieldOrderPin).Assembly.GetName().Name}. Available: " +
                string.Join(", ", typeof(SnapshotFieldOrderPin).Assembly.GetManifestResourceNames()));

        return JsonDocument.Parse(stream);
    }
}
