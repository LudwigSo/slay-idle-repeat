namespace SlayIdleRepeat.AssetManifest;

/// <summary>A delivery size in pixels.</summary>
public sealed record PixelSize(int Width, int Height)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Width}×{Height}";
}

/// <summary>A biome's locked six hues.</summary>
public sealed record Palette(
    string Base, string Shadow, string Accent, string Glow, string Prop, string Sky)
{
    public IReadOnlyList<string> Hues => [Base, Shadow, Accent, Glow, Prop, Sky];
}

/// <summary>One of the eight biomes.</summary>
public sealed record Biome(string Key, int Chapter, string DisplayName, Palette Palette);

/// <summary>A rarity code and its frame/gem colour.</summary>
public sealed record Rarity(string Code, string Colour);

/// <summary>A declared texture atlas.</summary>
/// <remarks>
/// <see cref="UncutAssetCount"/> is carried separately so an atlas emptied by a ruling stays
/// visible, e.g. a cut atlas with 32 members and 0 of them uncut.
/// </remarks>
public sealed record Atlas(string Id, string Contents, int AssetCount, int UncutAssetCount)
{
    /// <summary>True when this declaration governs the atlas an asset row names.</summary>
    /// <remarks>
    /// Most atlas ids are literal and match by equality. <c>atlas_biome_{n}</c> is a template
    /// standing for the eight per-chapter atlases (rows reference <c>atlas_biome_1</c>…<c>_8</c>,
    /// never the literal template) — resolved here on the declaration rather than re-derived by
    /// each caller, since two divergent copies once disagreed on which rows a chapter atlas covers.
    /// </remarks>
    /// <param name="atlasReference">The value an asset row carries in its <c>atlas</c> member.</param>
    public bool Covers(string atlasReference)
    {
        ArgumentNullException.ThrowIfNull(atlasReference);

        var open = Id.IndexOf('{', StringComparison.Ordinal);
        if (open < 0)
        {
            return string.Equals(Id, atlasReference, StringComparison.Ordinal);
        }

        var close = Id.IndexOf('}', StringComparison.Ordinal);
        if (close < open)
        {
            return false;
        }

        var prefix = Id[..open];
        var suffix = Id[(close + 1)..];

        if (atlasReference.Length <= prefix.Length + suffix.Length ||
            !atlasReference.StartsWith(prefix, StringComparison.Ordinal) ||
            !atlasReference.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        // Digits only, so a prefix match alone doesn't read e.g. `atlas_biome_props` as a member.
        var placeholder = atlasReference.AsSpan(prefix.Length, atlasReference.Length - prefix.Length - suffix.Length);
        foreach (var character in placeholder)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>One section, carrying the claimed count beside the count actually transcribed.</summary>
/// <remarks>
/// <see cref="ClaimedCount"/> and <see cref="TranscribedCount"/> are kept as separate fields on
/// purpose: collapsing them would destroy the evidence that a disagreement exists.
/// </remarks>
public sealed record ManifestSection(
    string Id,
    string Category,
    int ClaimedCount,
    int TranscribedCount,
    bool CountsAgree,
    int CutCount,
    int DerivedCount,
    string? PivotClass,
    string? Cut,
    string SourceSection,
    IReadOnlyList<string> Notes);

/// <summary>One SFX or music family.</summary>
public sealed record AudioFamily(
    string Id, string Label, int ClaimedCount, int TranscribedCount, bool CountsAgree,
    string SourceSection);

/// <summary>A recorded disagreement between this transcription and a claim in the design docs.</summary>
public sealed record Discrepancy(
    string Id, string SourceSection, string Claim, string Observed, string Detail);

/// <summary>One art asset slot.</summary>
public sealed record ArtAsset
{
    /// <summary>The stable id. Three later tasks key their records to it.</summary>
    public required string Id { get; init; }

    /// <summary>The section this row was transcribed from, e.g. <c>E12</c>.</summary>
    public required string Section { get; init; }

    /// <summary>The design-doc section(s) the row's values came from.</summary>
    public required string SourceSection { get; init; }

    /// <summary>
    /// True where the doc gave the group a count but named no individual asset, so this row's
    /// existence is inferred rather than authored.
    /// </summary>
    public required bool Derived { get; init; }

    /// <summary><c>doc</c> when the literal id occurs in a design doc; <c>convention</c> when built from the naming rule.</summary>
    public required string IdSource { get; init; }

    /// <summary>Non-null where a ruling removed the asset. The row is kept so totals stay derivable.</summary>
    public required string? Cut { get; init; }

    /// <summary>Delivery size, or null where no doc states one.</summary>
    public required PixelSize? DeliverySize { get; init; }

    /// <summary>Sprite pivot, or null where none applies to this class of asset.</summary>
    public required string? Pivot { get; init; }

    /// <summary>Atlas assignment, or null where none is assigned.</summary>
    public required string? Atlas { get; init; }

    /// <summary>The biome key where the asset is biome-scoped, else null.</summary>
    public required string? Biome { get; init; }

    /// <summary>The biome's locked palette where biome-scoped, else null.</summary>
    public required Palette? PaletteColours { get; init; }

    /// <summary>The subject descriptor the doc gives for prompting, or null where it gives none.</summary>
    public required string? Subject { get; init; }

    /// <summary>Every other transcribed field, by its manifest key (rarity, pose, gearSlot, …).</summary>
    public required IReadOnlyDictionary<string, string> Extra { get; init; }

    /// <summary>True when no ruling has removed this asset.</summary>
    public bool IsActive => Cut is null;

    /// <summary>The delivery size, or a loud failure — a null here is never defaulted at read time.</summary>
    public PixelSize RequireDeliverySize() =>
        DeliverySize ?? throw new InvalidOperationException(
            $"Asset '{Id}' ({Section}) has no delivery size: 15 §C states none for it. A null here " +
            "means the design docs do not authorise a value, and it must not be defaulted — see " +
            "the manifest's discrepancy DSC_MISSING_SIZES.");
}

/// <summary>One audio asset slot.</summary>
public sealed record AudioAsset
{
    /// <summary>The id — <c>mus_*</c> or <c>sfx_*</c>.</summary>
    public required string Id { get; init; }

    /// <summary><c>mus</c> or <c>sfx</c>.</summary>
    public required string Kind { get; init; }

    public required string Family { get; init; }

    /// <summary>The design-doc section the row came from.</summary>
    public required string SourceSection { get; init; }

    /// <summary>Use description. Null for SFX, which carries none.</summary>
    public required string? Use { get; init; }

    /// <summary>The prompt descriptor attached to this id, or null where none is attached.</summary>
    public required string? Descriptor { get; init; }

    /// <summary>Reserved for a descriptor covering a run of ids. Null throughout today.</summary>
    public required string? GroupDescriptor { get; init; }

    /// <summary>Per-track loop length in seconds. Music only.</summary>
    public required double? LengthSeconds { get; init; }

    /// <summary>A duration stated inline in the descriptor, else null.</summary>
    public required double? DurationSeconds { get; init; }

    public required string Format { get; init; }

    /// <summary>Whether this SFX ducks the music. Null for music, which does not duck itself.</summary>
    public required bool? DucksMusic { get; init; }

    public required string? Note { get; init; }

    /// <summary>True for a music track.</summary>
    public bool IsMusic => string.Equals(Kind, "mus", StringComparison.Ordinal);
}
