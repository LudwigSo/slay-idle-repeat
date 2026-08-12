using System.Globalization;
using System.Text.Json;

namespace SlayIdleRepeat.AssetManifest;

/// <summary>
/// Reads <c>game-data/assets/*.json</c> into the typed model.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 Explicit member-by-member parsing, not reflection binding. `game-data/README.md`: <em>"null
/// means the design docs do not authorise a value here … a hole that is null is greppable, and a
/// hole filled with a plausible-looking number is invisible."</em> A deserialiser silently turns an
/// absent member into <c>default</c>, which is exactly the coercion that rule forbids — so every
/// required member is fetched by name and a missing one throws
/// <see cref="AssetManifestFormatException"/> naming the pointer.
/// </para>
/// <para>
/// The schema in <c>game-data/schema/</c> is the authority on shape and is enforced at build time
/// by <c>tools/ContentValidator</c>, which validates every <c>*.json</c> under <c>game-data</c>.
/// This reader therefore fails loudly rather than defensively: if it throws, the schema and the
/// reader disagree, and that is a defect in one of them.
/// </para>
/// </remarks>
public static class AssetManifestReader
{
    /// <summary>The manifest directory, relative to the <c>game-data</c> root.</summary>
    public const string AssetsDirectory = "assets";

    /// <summary>The art manifest's file name.</summary>
    public const string ArtFileName = "asset_manifest_art.json";

    /// <summary>The audio manifest's file name.</summary>
    public const string AudioFileName = "asset_manifest_audio.json";

    /// <summary>Loads both manifests from a <c>game-data</c> root directory.</summary>
    public static AssetManifestSet Load(string gameDataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDataRoot);

        var directory = Path.Combine(gameDataRoot, AssetsDirectory);
        if (!Directory.Exists(directory))
        {
            throw new AssetManifestFormatException(
                AssetsDirectory,
                $"there is no manifest directory at '{directory}'. The register is authored by " +
                "M8-09 and three later tasks read it.");
        }

        return LoadFrom(
            File.ReadAllText(Path.Combine(directory, ArtFileName)),
            File.ReadAllText(Path.Combine(directory, AudioFileName)));
    }

    /// <summary>Loads both manifests from their JSON text — the seam the tests drive.</summary>
    public static AssetManifestSet LoadFrom(string artJson, string audioJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(audioJson);

        using var art = JsonDocument.Parse(artJson);
        using var audio = JsonDocument.Parse(audioJson);
        return new AssetManifestSet(ReadArt(art.RootElement), ReadAudio(audio.RootElement));
    }

    private static ArtManifest ReadArt(JsonElement root)
    {
        var biomes = Read(root, "biomes").EnumerateArray()
            .Select(b => new Biome(
                Text(b, "key", "biomes"),
                Int(b, "chapter", "biomes"),
                Text(b, "displayName", "biomes"),
                ReadPalette(Read(b, "palette"), "biomes")))
            .ToArray();

        var rarities = Read(root, "rarities").EnumerateArray()
            .Select(r => new Rarity(Text(r, "code", "rarities"), Text(r, "colour", "rarities")))
            .ToArray();

        var atlases = Read(root, "atlases").EnumerateArray()
            .Select(a => new Atlas(
                Text(a, "id", "atlases"), Text(a, "contents", "atlases"),
                Int(a, "assetCount", "atlases"), Int(a, "uncutAssetCount", "atlases")))
            .ToArray();

        var sections = Read(root, "sections").EnumerateArray()
            .Select(s => new ManifestSection(
                Text(s, "id", "sections"),
                Text(s, "category", "sections"),
                Int(s, "claimedCount", "sections"),
                Int(s, "transcribedCount", "sections"),
                Bool(s, "countsAgree", "sections"),
                Int(s, "cutCount", "sections"),
                Int(s, "derivedCount", "sections"),
                NullableText(s, "pivotClass", "sections"),
                NullableText(s, "cut", "sections"),
                Text(s, "sourceSection", "sections"),
                Read(s, "notes").EnumerateArray().Select(n => n.GetString()!).ToArray()))
            .ToArray();

        var assets = Read(root, "assets").EnumerateArray().Select(ReadArtAsset).ToArray();

        var totals = Read(root, "totals");
        return new ArtManifest(
            Text(root, "_status", "(root)"),
            new ArtTotals(
                Int(totals, "claimedBySummaryTable", "totals"),
                Int(totals, "transcribed", "totals"),
                Int(totals, "cut", "totals"),
                Int(totals, "active", "totals"),
                Int(totals, "derived", "totals")),
            biomes, rarities, atlases, sections,
            ReadDiscrepancies(root), assets);
    }

    private static ArtAsset ReadArtAsset(JsonElement a)
    {
        var id = Text(a, "id", "assets");
        var extra = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in a.EnumerateObject())
        {
            if (KnownArtMembers.Contains(member.Name) || member.Value.ValueKind == JsonValueKind.Null)
            {
                continue;
            }

            extra[member.Name] = member.Value.ValueKind switch
            {
                JsonValueKind.String => member.Value.GetString()!,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Number => member.Value.GetRawText(),
                _ => member.Value.GetRawText(),
            };
        }

        return new ArtAsset
        {
            Id = id,
            Section = Text(a, "section", id),
            SourceSection = Text(a, "sourceSection", id),
            Derived = Bool(a, "derived", id),
            IdSource = Text(a, "idSource", id),
            Cut = NullableText(a, "cut", id),
            DeliverySize = ReadSize(a, id),
            Pivot = NullableText(a, "pivot", id),
            Atlas = NullableText(a, "atlas", id),
            Biome = NullableText(a, "biome", id),
            PaletteColours = Read(a, "palette").ValueKind == JsonValueKind.Null
                ? null
                : ReadPalette(Read(a, "palette"), id),
            Subject = NullableText(a, "subject", id),
            Extra = extra,
        };
    }

    private static readonly HashSet<string> KnownArtMembers = new(StringComparer.Ordinal)
    {
        "id", "section", "sourceSection", "derived", "idSource", "cut", "deliverySize",
        "pivot", "atlas", "biome", "palette", "subject",
    };

    private static PixelSize? ReadSize(JsonElement a, string owner)
    {
        var size = Read(a, "deliverySize", owner);
        return size.ValueKind == JsonValueKind.Null
            ? null
            : new PixelSize(Int(size, "width", owner), Int(size, "height", owner));
    }

    private static Palette ReadPalette(JsonElement p, string owner) => new(
        Text(p, "base", owner), Text(p, "shadow", owner), Text(p, "accent", owner),
        Text(p, "glow", owner), Text(p, "prop", owner), Text(p, "sky", owner));

    private static AudioManifest ReadAudio(JsonElement root)
    {
        var families = Read(root, "families").EnumerateArray()
            .Select(f => new AudioFamily(
                Text(f, "id", "families"), Text(f, "label", "families"),
                Int(f, "claimedCount", "families"), Int(f, "transcribedCount", "families"),
                Bool(f, "countsAgree", "families"), Text(f, "sourceSection", "families")))
            .ToArray();

        var assets = Read(root, "assets").EnumerateArray().Select(a =>
        {
            var id = Text(a, "id", "assets");
            return new AudioAsset
            {
                Id = id,
                Kind = Text(a, "kind", id),
                Family = Text(a, "family", id),
                SourceSection = Text(a, "sourceSection", id),
                Use = NullableText(a, "use", id),
                Descriptor = NullableText(a, "descriptor", id),
                GroupDescriptor = NullableText(a, "groupDescriptor", id),
                LengthSeconds = NullableNumber(a, "lengthSeconds", id),
                DurationSeconds = NullableNumber(a, "durationSeconds", id),
                Format = Text(a, "format", id),
                DucksMusic = a.TryGetProperty("ducksMusic", out var duck)
                    ? duck.GetBoolean()
                    : null,
                Note = NullableText(a, "note", id),
            };
        }).ToArray();

        var totals = Read(root, "totals");
        return new AudioManifest(
            Text(root, "_status", "(root)"),
            new AudioTotals(
                Int(totals, "claimedMusic", "totals"),
                Int(totals, "claimedSfx", "totals"),
                Int(totals, "claimedCombined", "totals"),
                Int(totals, "transcribedMusic", "totals"),
                Int(totals, "transcribedSfx", "totals"),
                Int(totals, "transcribedCombined", "totals"),
                Int(totals, "sfxWithoutDescriptor", "totals")),
            families, ReadDiscrepancies(root), assets);
    }

    private static IReadOnlyList<Discrepancy> ReadDiscrepancies(JsonElement root) =>
        Read(root, "discrepancies").EnumerateArray()
            .Select(d => new Discrepancy(
                Text(d, "id", "discrepancies"), Text(d, "sourceSection", "discrepancies"),
                Text(d, "claim", "discrepancies"), Text(d, "observed", "discrepancies"),
                Text(d, "detail", "discrepancies")))
            .ToArray();

    // ------------------------------------------------------------------ member access
    private static JsonElement Read(JsonElement parent, string name, string owner = "(root)") =>
        parent.TryGetProperty(name, out var value)
            ? value
            : throw new AssetManifestFormatException(
                owner, $"has no member '{name}'. The schema requires it; a reader that defaulted it " +
                       "would hide the hole instead of reporting it.");

    private static string Text(JsonElement parent, string name, string owner) =>
        Read(parent, name, owner).GetString()
        ?? throw new AssetManifestFormatException(owner, $"member '{name}' is null, but it is required.");

    private static string? NullableText(JsonElement parent, string name, string owner)
    {
        var value = Read(parent, name, owner);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetString();
    }

    private static double? NullableNumber(JsonElement parent, string name, string owner)
    {
        var value = Read(parent, name, owner);
        return value.ValueKind == JsonValueKind.Null ? null : value.GetDouble();
    }

    private static int Int(JsonElement parent, string name, string owner) =>
        Read(parent, name, owner).GetInt32();

    private static bool Bool(JsonElement parent, string name, string owner) =>
        Read(parent, name, owner).GetBoolean();

    internal static string Format(double value) =>
        value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The manifest on disk does not have the shape the reader and schema agree on.</summary>
public sealed class AssetManifestFormatException(string location, string reason)
    : InvalidOperationException($"{location} {reason}")
{
    /// <summary>Where in the manifest the problem is.</summary>
    public string Location { get; } = location;
}
