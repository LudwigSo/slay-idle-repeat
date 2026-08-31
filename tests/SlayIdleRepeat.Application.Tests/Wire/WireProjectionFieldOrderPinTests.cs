using System.Text.Json;
using Shouldly;
using SlayIdleRepeat.Application.Wire;
using SlayIdleRepeat.Core.Model.Snapshots;
using Xunit;

namespace SlayIdleRepeat.Application.Tests.Wire;

/// <summary>
/// The wire projection's field-order pin — <c>SnapshotFieldOrder.json</c>'s discipline applied to
/// the two projection records: the pinned list is produced by <c>CanonicalStateWriter</c>'s OWN
/// traversal (<c>CanonicalFieldOrder</c>), so it cannot drift from the bytes the wire
/// <c>stateHash</c> is computed over.
/// </summary>
/// <remarks>
/// Keyed by <c>SnapshotSchema.SchemaVersion</c>, because the projection is a projection OF the
/// snapshots: a snapshot change bumps the schema version first, and the projection answers for it
/// under the new key. A violation at an existing key is a wire-serialisation change: every client
/// mirror's hash breaks. Fix the projection or add the new version's pin — never edit an existing
/// pinned list.
/// </remarks>
public sealed class WireProjectionFieldOrderPinTests
{
    private const string ResourceName =
        "SlayIdleRepeat.Application.Tests.Wire.WireProjectionFieldOrder.json";

    /// <summary>The projections under pin. Explicit rather than scanned: the wire has exactly these two shapes.</summary>
    private static readonly (string Name, Type Type)[] Projections =
    {
        (nameof(PlayerWireProjection), typeof(PlayerWireProjection)),
        (nameof(RunWireProjection), typeof(RunWireProjection)),
    };

    [Fact]
    public void The_current_schema_version_has_a_pin_for_both_projections()
    {
        var pinned = For(SnapshotSchema.SchemaVersion);

        foreach (var (name, _) in Projections)
        {
            pinned.ContainsKey(name).ShouldBeTrue(
                $"WireProjectionFieldOrder.json pins no field list for {name} at SchemaVersion " +
                $"{SnapshotSchema.SchemaVersion} — the wire stateHash over it is unguarded");
        }
    }

    [Fact]
    public void Every_projection_matches_its_pinned_field_order_exactly()
    {
        var pinned = For(SnapshotSchema.SchemaVersion);
        var offenders = new List<string>();

        foreach (var (name, type) in Projections)
        {
            var actual = CanonicalStateWriter.CanonicalFieldOrder(type);
            var expected = pinned.TryGetValue(name, out var list) ? list : Array.Empty<string>();

            // 🔴 The floor on both sides, before the comparison. This loop runs to
            // Math.Max(actual, expected), so it iterates zero times over two empty lists — a
            // CanonicalFieldOrder that stopped reflecting and a pinned entry that lost its fields
            // would agree perfectly and report every wire projection guarded.
            actual.Count.ShouldBeGreaterThan(
                2,
                $"{name} reflected {actual.Count} canonical field(s). A projection the writer cannot " +
                "see is a projection whose field order nothing pins.");

            expected.Count.ShouldBeGreaterThan(
                2,
                $"WireProjectionFieldOrder.json pins {expected.Count} field(s) for {name} at " +
                $"SchemaVersion {SnapshotSchema.SchemaVersion}. An empty pinned list matches nothing " +
                "and refuses nothing.");

            for (var i = 0; i < Math.Max(actual.Count, expected.Count); i++)
            {
                var pinnedField = i < expected.Count ? expected[i] : "<no field>";
                var actualField = i < actual.Count ? actual[i] : "<no field>";

                if (!string.Equals(pinnedField, actualField, StringComparison.Ordinal))
                {
                    offenders.Add($"{name} field {i}: pinned '{pinnedField}', found '{actualField}'");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "This is a WIRE-SERIALISATION CHANGE: adding, removing or reordering a projection " +
            "field changes every wire stateHash in existence and every client mirror breaks on it. " +
            "Change the projection deliberately — with the snapshot change that motivated it and " +
            "its SchemaVersion bump — and pin the new version; never edit an existing pinned list.");
    }

    [Fact]
    public void The_pin_bites_on_an_added_removed_and_reordered_field()
    {
        // The pin's own teeth (S1/S4): a deliberately wrong list is caught in all three shapes,
        // proven without committing a violating projection.
        var actual = CanonicalStateWriter.CanonicalFieldOrder(typeof(RunWireProjection)).ToList();

        Compare(actual, actual).ShouldBeEmpty("the control: the real list against itself");

        var removed = actual.ToList();
        removed.RemoveAt(3);
        Compare(removed, actual).ShouldNotBeEmpty("a removed field must be caught");

        var added = actual.ToList();
        added.Insert(3, "Smuggled:System.Int64");
        Compare(added, actual).ShouldNotBeEmpty("an added field must be caught");

        var reordered = actual.ToList();
        (reordered[0], reordered[1]) = (reordered[1], reordered[0]);
        Compare(reordered, actual).ShouldNotBeEmpty("a reordered field must be caught");
    }

    private static IReadOnlyList<string> Compare(IReadOnlyList<string> pinned, IReadOnlyList<string> actual)
    {
        var offenders = new List<string>();
        for (var i = 0; i < Math.Max(actual.Count, pinned.Count); i++)
        {
            var pinnedField = i < pinned.Count ? pinned[i] : "<no field>";
            var actualField = i < actual.Count ? actual[i] : "<no field>";
            if (!string.Equals(pinnedField, actualField, StringComparison.Ordinal))
            {
                offenders.Add($"field {i}: pinned '{pinnedField}', found '{actualField}'");
            }
        }

        return offenders;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> For(int schemaVersion)
    {
        using var stream = typeof(WireProjectionFieldOrderPinTests).Assembly
            .GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The pin '{ResourceName}' is not embedded in this assembly. Available: " +
                string.Join(
                    ", ", typeof(WireProjectionFieldOrderPinTests).Assembly.GetManifestResourceNames()));

        using var document = JsonDocument.Parse(stream);

        var pin = document.RootElement.GetProperty("pins")
            .EnumerateArray()
            .SingleOrDefault(p => p.GetProperty("schemaVersion").GetInt32() == schemaVersion);

        if (pin.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException(
                $"WireProjectionFieldOrder.json pins nothing for SchemaVersion {schemaVersion}. " +
                "Add the section for the new version; never edit an existing one.");
        }

        return pin.GetProperty("projections")
            .EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => (IReadOnlyList<string>)property.Value
                    .EnumerateArray()
                    .Select(element => element.GetString()!)
                    .ToArray(),
                StringComparer.Ordinal);
    }
}
