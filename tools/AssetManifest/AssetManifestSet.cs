namespace SlayIdleRepeat.AssetManifest;

/// <summary>`15` §E1's claimed total beside the transcribed one.</summary>
public sealed record ArtTotals(
    int ClaimedBySummaryTable, int Transcribed, int Cut, int Active, int Derived);

/// <summary>Doc 20's claimed totals beside the transcribed ones.</summary>
public sealed record AudioTotals(
    int ClaimedMusic, int ClaimedSfx, int ClaimedCombined,
    int TranscribedMusic, int TranscribedSfx, int TranscribedCombined,
    int SfxWithoutDescriptor);

/// <summary>The art register (`15` §E2–§E21).</summary>
public sealed record ArtManifest(
    string Status,
    ArtTotals Totals,
    IReadOnlyList<Biome> Biomes,
    IReadOnlyList<Rarity> Rarities,
    IReadOnlyList<Atlas> Atlases,
    IReadOnlyList<ManifestSection> Sections,
    IReadOnlyList<Discrepancy> Discrepancies,
    IReadOnlyList<ArtAsset> Assets);

/// <summary>The audio register (`20` §3, §4).</summary>
public sealed record AudioManifest(
    string Status,
    AudioTotals Totals,
    IReadOnlyList<AudioFamily> Families,
    IReadOnlyList<Discrepancy> Discrepancies,
    IReadOnlyList<AudioAsset> Assets);

/// <summary>
/// Both manifests, with the lookups the three downstream consumers need.
/// </summary>
/// <remarks>
/// M8-01a keys provenance records to <see cref="ArtAsset.Id"/> / <see cref="AudioAsset.Id"/>;
/// M8-06 reads delivery size, pivot and atlas; M8-10 enumerates the rows to emit one placeholder
/// per slot. All three want the same thing from this type: a stable, complete, queryable id list.
/// </remarks>
public sealed class AssetManifestSet
{
    private readonly Dictionary<string, ArtAsset> _artById;
    private readonly Dictionary<string, AudioAsset> _audioById;

    internal AssetManifestSet(ArtManifest art, AudioManifest audio)
    {
        Art = art;
        Audio = audio;

        // 🔒 Ordinal, and built with Add rather than ToDictionary's last-wins: a duplicate id is a
        // defect the ManifestValidator reports, and a silently collapsed dictionary would hide it
        // from every consumer that then reads a complete-looking register.
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
    }

    /// <summary>The art register.</summary>
    public ArtManifest Art { get; }

    /// <summary>The audio register.</summary>
    public AudioManifest Audio { get; }

    /// <summary>Every asset id in the register, art then audio, in manifest order.</summary>
    public IReadOnlyList<string> AllIds =>
        [.. Art.Assets.Select(a => a.Id), .. Audio.Assets.Select(a => a.Id)];

    /// <summary>Every art asset a ruling has not cut.</summary>
    public IEnumerable<ArtAsset> ActiveArt => Art.Assets.Where(a => a.IsActive);

    /// <summary>Every art asset a ruling has cut, with the ruling that cut it.</summary>
    public IEnumerable<ArtAsset> CutArt => Art.Assets.Where(a => !a.IsActive);

    /// <summary>Every art row whose existence the docs implied by a count rather than naming it.</summary>
    public IEnumerable<ArtAsset> DerivedArt => Art.Assets.Where(a => a.Derived);

    /// <summary>The art assets of one §E-section.</summary>
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

    /// <summary>The uncut art assets packed into one atlas (`15` §D2).</summary>
    public IEnumerable<ArtAsset> AtlasMembers(string atlas)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(atlas);
        return Art.Assets.Where(a => a.IsActive && string.Equals(a.Atlas, atlas, StringComparison.Ordinal));
    }

    /// <summary>An art asset by id, or null.</summary>
    public ArtAsset? FindArt(string id) => _artById.GetValueOrDefault(id);

    /// <summary>An audio asset by id, or null.</summary>
    public AudioAsset? FindAudio(string id) => _audioById.GetValueOrDefault(id);

    /// <summary>
    /// An art asset by id, or a loud failure naming the id. The lookup a provenance record or a
    /// placeholder generator wants: an unknown id is a bug in the caller, not an empty result.
    /// </summary>
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
