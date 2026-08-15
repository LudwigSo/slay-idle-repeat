using System.Globalization;
using System.Text.Json;
using Xunit;

namespace SlayIdleRepeat.Core.Tests.Model.Snapshots;

/// <summary>
/// Reads <c>CanonicalStateWriterReferenceVectors.json</c> — the committed determinism pin — out
/// of this assembly's embedded resources.
/// </summary>
/// <remarks>
/// Rows are expectations, not output of the code under test: nothing here re-derives a hash or a
/// byte, it only parses.
/// </remarks>
internal static class CanonicalReferenceVectors
{
    private const string ResourceName =
        "SlayIdleRepeat.Core.Tests.Model.Snapshots.CanonicalStateWriterReferenceVectors.json";

    private static readonly JsonDocument Document = Load();

    /// <summary>Landon Curt Noll's published FNV-1a 64 vectors — the external check.</summary>
    internal static IReadOnlyList<PublishedRow> Published { get; } = ReadPublished();

    internal static IReadOnlyList<CanonicalRow> Canonical { get; } = ReadCanonical();

    internal static ulong OffsetBasis { get; } = ParseHex(
        Document.RootElement.GetProperty("fnv1a64PublishedKnownAnswerVectors")
            .GetProperty("offsetBasis").GetString()!);

    internal static ulong Prime { get; } = ParseHex(
        Document.RootElement.GetProperty("fnv1a64PublishedKnownAnswerVectors")
            .GetProperty("prime").GetString()!);

    internal static CanonicalRow Row(string id) =>
        Canonical.Single(row => row.Id.Equals(id, StringComparison.Ordinal));

    internal static TheoryData<string> CanonicalIds() => IdsOf(Canonical);

    /// <summary>
    /// The canonical row ids of one hashing mode, as xUnit theory data — so a theory can drive a
    /// single public entry point instead of branching on the row's mode in its own body.
    /// </summary>
    /// <param name="mode"><c>"meta"</c> or <c>"run"</c>.</param>
    internal static TheoryData<string> CanonicalIds(string mode) =>
        IdsOf(Canonical.Where(row => row.Mode.Equals(mode, StringComparison.Ordinal)));

    private static TheoryData<string> IdsOf(IEnumerable<CanonicalRow> rows)
    {
        var data = new TheoryData<string>();
        foreach (var row in rows)
        {
            data.Add(row.Id);
        }

        return data;
    }

    internal static TheoryData<string, ulong> PublishedRows()
    {
        var data = new TheoryData<string, ulong>();
        foreach (var row in Published)
        {
            data.Add(row.Input, row.Hash);
        }

        return data;
    }

    private static JsonDocument Load()
    {
        using var stream = typeof(CanonicalReferenceVectors).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The determinism pin '{ResourceName}' is not embedded in " +
                $"{typeof(CanonicalReferenceVectors).Assembly.GetName().Name}. Available: " +
                string.Join(", ", typeof(CanonicalReferenceVectors).Assembly.GetManifestResourceNames()));

        return JsonDocument.Parse(stream);
    }

    private static ulong ParseHex(string text) =>
        ulong.Parse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static IReadOnlyList<PublishedRow> ReadPublished() =>
        Document.RootElement
            .GetProperty("fnv1a64PublishedKnownAnswerVectors")
            .GetProperty("rows")
            .EnumerateArray()
            .Select(element => new PublishedRow(
                element.GetProperty("input").GetString()!,
                ParseHex(element.GetProperty("hash").GetString()!)))
            .ToArray();

    private static IReadOnlyList<CanonicalRow> ReadCanonical() =>
        Document.RootElement
            .GetProperty("canonicalStateVectors")
            .GetProperty("rows")
            .EnumerateArray()
            .Select(element => new CanonicalRow(
                element.GetProperty("id").GetString()!,
                element.GetProperty("why").GetString()!,
                element.GetProperty("mode").GetString()!,
                element.GetProperty("encodedByteLength").GetInt32(),
                element.GetProperty("encodedHex").GetString()!,
                ParseHex(element.GetProperty("hash").GetString()!),
                element.GetProperty("wire").GetString()!))
            .ToArray();

    /// <summary>One published FNV-1a 64 known-answer vector over raw UTF-8 bytes.</summary>
    internal sealed record PublishedRow(string Input, ulong Hash);

    internal sealed record CanonicalRow(
        string Id,
        string Why,
        string Mode,
        int EncodedByteLength,
        string EncodedHex,
        ulong Hash,
        string Wire);
}
