namespace SlayIdleRepeat.AssetManifest;

/// <summary>A delivery size in pixels (`15` §C).</summary>
public sealed record PixelSize(int Width, int Height)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Width}×{Height}";
}

/// <summary>A biome's locked six hues (`15` §A5).</summary>
public sealed record Palette(
    string Base, string Shadow, string Accent, string Glow, string Prop, string Sky)
{
    /// <summary>The six hues in the order `15` §A5 tabulates them.</summary>
    public IReadOnlyList<string> Hues => [Base, Shadow, Accent, Glow, Prop, Sky];
}

/// <summary>One of the eight biomes (`15` §A5).</summary>
public sealed record Biome(string Key, int Chapter, string DisplayName, Palette Palette);

/// <summary>A rarity code and its frame/gem colour (`15` §A5).</summary>
public sealed record Rarity(string Code, string Colour);

/// <summary>A texture atlas declared by `15` §D2.</summary>
/// <remarks>
/// <see cref="UncutAssetCount"/> is carried separately so an atlas emptied by a ruling stays
/// visible: after the O8 ruling <c>atlas_vfx</c> has 32 members and 0 of them uncut.
/// </remarks>
public sealed record Atlas(string Id, string Contents, int AssetCount, int UncutAssetCount);

/// <summary>
/// One `15` §E-section, carrying §E1's claimed count beside the count actually transcribed.
/// </summary>
/// <remarks>
/// 🔒 <see cref="ClaimedCount"/> and <see cref="TranscribedCount"/> are separate fields on purpose.
/// Reconciling the manifest totals is O30's job at M11-01; collapsing them here would destroy the
/// evidence that a disagreement exists.
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

/// <summary>One SFX or music family (`20` §3, §4.1–§4.6).</summary>
public sealed record AudioFamily(
    string Id, string Label, int ClaimedCount, int TranscribedCount, bool CountsAgree,
    string SourceSection);

/// <summary>A recorded disagreement between this transcription and a claim in the design docs.</summary>
public sealed record Discrepancy(
    string Id, string SourceSection, string Claim, string Observed, string Detail);

/// <summary>One art asset slot (`15` §E2–§E21).</summary>
public sealed record ArtAsset
{
    /// <summary>The `15` §D1 id. Stable — three later tasks key their records to it.</summary>
    public required string Id { get; init; }

    /// <summary>The §E-section this row was transcribed from, e.g. <c>E12</c>.</summary>
    public required string Section { get; init; }

    /// <summary>The design-doc section(s) the row's values came from.</summary>
    public required string SourceSection { get; init; }

    /// <summary>
    /// 🔒 True where the doc gave the group a count but named no individual asset, so this row's
    /// existence is inferred rather than authored. Lets a reconciliation find every id no human wrote.
    /// </summary>
    public required bool Derived { get; init; }

    /// <summary><c>doc</c> when the literal id occurs in a design doc; <c>convention</c> when built from §D1.</summary>
    public required string IdSource { get; init; }

    /// <summary>Non-null where a ruling removed the asset. The row is kept so totals stay derivable.</summary>
    public required string? Cut { get; init; }

    /// <summary>`15` §C delivery size, or null where no doc states one.</summary>
    public required PixelSize? DeliverySize { get; init; }

    /// <summary>`15` §C pivot, or null where §C states none for this class of asset.</summary>
    public required string? Pivot { get; init; }

    /// <summary>`15` §D2 atlas, or null where §D2 assigns none.</summary>
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

    /// <summary>
    /// The delivery size, or a loud failure. 🔒 `game-data/README.md`: a null is "the design docs do
    /// not authorise a value here" and is never coerced to a default at read time.
    /// </summary>
    public PixelSize RequireDeliverySize() =>
        DeliverySize ?? throw new InvalidOperationException(
            $"Asset '{Id}' ({Section}) has no delivery size: 15 §C states none for it. A null here " +
            "means the design docs do not authorise a value, and it must not be defaulted — see " +
            "the manifest's discrepancy DSC_MISSING_SIZES.");
}

/// <summary>One audio asset slot (`20` §3, §4).</summary>
public sealed record AudioAsset
{
    /// <summary>The `20` §5 id — <c>mus_*</c> or <c>sfx_*</c>.</summary>
    public required string Id { get; init; }

    /// <summary><c>mus</c> or <c>sfx</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The `20` §3/§4.x family.</summary>
    public required string Family { get; init; }

    /// <summary>The design-doc section the row came from.</summary>
    public required string SourceSection { get; init; }

    /// <summary>`20` §3's Use column. Null for SFX, which §4 gives none.</summary>
    public required string? Use { get; init; }

    /// <summary>The prompt descriptor doc 20 attaches to this id, or null where it attaches none.</summary>
    public required string? Descriptor { get; init; }

    /// <summary>Reserved for a descriptor a human rules as covering a run of ids. Null throughout today.</summary>
    public required string? GroupDescriptor { get; init; }

    /// <summary>`20` §3's per-track loop length in seconds. Music only.</summary>
    public required double? LengthSeconds { get; init; }

    /// <summary>A duration doc 20 states inline in the descriptor, else null.</summary>
    public required double? DurationSeconds { get; init; }

    /// <summary>`20` §5 delivery format for this kind.</summary>
    public required string Format { get; init; }

    /// <summary>`20` §5's Ducking row. Null for music, which does not duck itself.</summary>
    public required bool? DucksMusic { get; init; }

    /// <summary>Anything the doc says about the row beyond its descriptor.</summary>
    public required string? Note { get; init; }

    /// <summary>True for a music track.</summary>
    public bool IsMusic => string.Equals(Kind, "mus", StringComparison.Ordinal);
}
