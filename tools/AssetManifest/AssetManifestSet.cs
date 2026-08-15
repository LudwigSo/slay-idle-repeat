namespace SlayIdleRepeat.AssetManifest;

/// <summary>The claimed total beside the transcribed one.</summary>
public sealed record ArtTotals(
    int ClaimedBySummaryTable, int Transcribed, int Cut, int Active, int Derived);

/// <summary>The claimed totals beside the transcribed ones.</summary>
public sealed record AudioTotals(
    int ClaimedMusic, int ClaimedSfx, int ClaimedCombined,
    int TranscribedMusic, int TranscribedSfx, int TranscribedCombined,
    int SfxWithoutDescriptor);

/// <summary>The art register.</summary>
public sealed record ArtManifest(
    string Status,
    ArtTotals Totals,
    IReadOnlyList<Biome> Biomes,
    IReadOnlyList<Rarity> Rarities,
    IReadOnlyList<Atlas> Atlases,
    IReadOnlyList<ManifestSection> Sections,
    IReadOnlyList<Discrepancy> Discrepancies,
    IReadOnlyList<ArtAsset> Assets);

/// <summary>The audio register.</summary>
public sealed record AudioManifest(
    string Status,
    AudioTotals Totals,
    IReadOnlyList<AudioFamily> Families,
    IReadOnlyList<Discrepancy> Discrepancies,
    IReadOnlyList<AudioAsset> Assets);

/// <summary>Both manifests, with id lookups over the combined asset list.</summary>
public sealed class AssetManifestSet
{
    private readonly Dictionary<string, ArtAsset> _artById;
    private readonly Dictionary<string, AudioAsset> _audioById;
    private readonly string[] _allIds;

    internal AssetManifestSet(ArtManifest art, AudioManifest audio)
    {
        Art = art;
        Audio = audio;

        // TryAdd, not ToDictionary: a duplicate id is a defect ManifestValidator reports, and
        // last-wins would silently hide it behind a complete-looking register.
        _artById = new Dictionary<string, ArtAsset>(StringComparer.Ordinal);
        foreach (var asset in art.Assets)
        {
            _artById.TryAdd(asset.Id, asset);
        }

        _audioById = new Dictionary<string, AudioAsset>(StringComparer.Ordinal);
        foreach (var asset in audio.Assets)
        {
            _audioById.TryAdd(asset.Id, asset);
        }

        // Built once and cached: this list is walked repeatedly over ~1,080 rows.
        _allIds = [.. art.Assets.Select(a => a.Id), .. audio.Assets.Select(a => a.Id)];
    }

    public ArtManifest Art { get; }

    public AudioManifest Audio { get; }

    /// <summary>Every asset id in the register, art then audio, in manifest order.</summary>
    public IReadOnlyList<string> AllIds => _allIds;

    /// <summary>Every art asset a ruling has not cut.</summary>
    public IEnumerable<ArtAsset> ActiveArt => Art.Assets.Where(a => a.IsActive);

    /// <summary>Every art asset a ruling has cut, with the ruling that cut it.</summary>
    public IEnumerable<ArtAsset> CutArt => Art.Assets.Where(a => !a.IsActive);

    /// <summary>Every art row whose existence the docs implied by a count rather than naming it.</summary>
    public IEnumerable<ArtAsset> DerivedArt => Art.Assets.Where(a => a.Derived);

    /// <summary>The art assets of one section.</summary>
    public IEnumerable<ArtAsset> ArtInSection(string section)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(section);
        return Art.Assets.Where(a => string.Equals(a.Section, section, StringComparison.Ordinal));
    }

    /// <summary>The audio assets of one family.</summary>
    public IEnumerable<AudioAsset> AudioInFamily(string family)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(family);
        return Audio.Assets.Where(a => string.Equals(a.Family, family, StringComparison.Ordinal));
    }

    /// <summary>The uncut art assets packed into one atlas.</summary>
    /// <remarks>
    /// Accepts either a declared atlas id or the concrete reference a row carries, resolving a
    /// templated id like <c>atlas_biome_{n}</c> through <see cref="Atlas.Covers"/> — comparing by
    /// equality alone left the per-chapter atlases looking empty.
    /// </remarks>
    /// <param name="atlas">A declared atlas id, or the value an asset row carries in <c>atlas</c>.</param>
    public IEnumerable<ArtAsset> AtlasMembers(string atlas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(atlas);

        var declared = Art.Atlases.FirstOrDefault(a => string.Equals(a.Id, atlas, StringComparison.Ordinal));

        return declared is null
            ? Art.Assets.Where(a => a.IsActive && string.Equals(a.Atlas, atlas, StringComparison.Ordinal))
            : Art.Assets.Where(a => a.IsActive && a.Atlas is not null && declared.Covers(a.Atlas));
    }

    public ArtAsset? FindArt(string id) => _artById.GetValueOrDefault(id);

    public AudioAsset? FindAudio(string id) => _audioById.GetValueOrDefault(id);

    /// <summary>An art asset by id, or a loud failure: an unknown id is a bug in the caller.</summary>
    public ArtAsset RequireArt(string id) =>
        FindArt(id) ?? throw new KeyNotFoundException(
            $"No art asset '{id}' in the manifest. It holds {Art.Assets.Count} rows across " +
            $"{Art.Sections.Count} sections of 15 §E.");

    /// <summary>An audio asset by id, or a loud failure naming the id.</summary>
    public AudioAsset RequireAudio(string id) =>
        FindAudio(id) ?? throw new KeyNotFoundException(
            $"No audio asset '{id}' in the manifest. It holds {Audio.Assets.Count} rows across " +
            $"{Audio.Families.Count} families of 20 §3/§4.");
}
