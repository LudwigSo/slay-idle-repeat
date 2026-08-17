namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// What the boot's atlas stage found: a manifest it read, or a stated reason there was none.
/// </summary>
/// <remarks>
/// <para>
/// Absence is an ordinary outcome rather than a failure. The placeholder generator's output is
/// gitignored and has never been produced in this checkout, so "no atlas" is the state of every
/// machine the game is built on — a boot that treated it as fatal would be a game nobody can start.
/// </para>
/// <para>
/// ⚠️ It is also permanent for the placeholder pipeline as it stands: that generator emits loose
/// per-asset images and a rectangles-only manifest, and never rasterises an atlas page. There is no
/// atlas texture for this stage to load, by construction — what it reads is the manifest.
/// </para>
/// </remarks>
public sealed class BootAtlasResult
{
    private BootAtlasResult(bool isAvailable, int atlasCount, int placementCount, string detail)
    {
        IsAvailable = isAvailable;
        AtlasCount = atlasCount;
        PlacementCount = placementCount;
        Detail = detail;
    }

    /// <summary>Whether an atlas manifest was actually read.</summary>
    public bool IsAvailable { get; }

    /// <summary>How many atlas manifests were read. Zero on an absent result.</summary>
    public int AtlasCount { get; }

    /// <summary>How many placements those manifests hold in total. Zero on an absent result.</summary>
    public int PlacementCount { get; }

    /// <summary>What was read, or why nothing was.</summary>
    public string Detail { get; }

    /// <summary>An atlas that was read, with what it held.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Either count is negative.</exception>
    public static BootAtlasResult Loaded(int atlasCount, int placementCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(atlasCount);
        ArgumentOutOfRangeException.ThrowIfNegative(placementCount);

        return new BootAtlasResult(
            isAvailable: true,
            atlasCount,
            placementCount,
            $"{atlasCount} atlas manifest(s) carrying {placementCount} placement(s)");
    }

    /// <summary>No atlas, and the reason this particular read produced none.</summary>
    /// <remarks>
    /// The reason is the only trace a non-fatal absence leaves, which is why a blank one is refused:
    /// "no atlas" reads identically for a checkout that never generated one and a generator run that
    /// silently produced nothing, and those are fixed by different people.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="detail"/> is null, empty or whitespace.</exception>
    public static BootAtlasResult Absent(string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);

        return new BootAtlasResult(isAvailable: false, atlasCount: 0, placementCount: 0, detail);
    }

    /// <inheritdoc/>
    public override string ToString() => IsAvailable ? $"loaded — {Detail}" : $"absent — {Detail}";
}

/// <summary>
/// The boot's one question about the placeholder atlas: what is there?
/// </summary>
/// <remarks>
/// Synchronous, because the answer is a directory listing and a small parse rather than a network
/// call. A read that finds nothing answers <see cref="BootAtlasResult.Absent"/>; a read that finds a
/// manifest it cannot make sense of throws, and the boot reports that as the unanticipated failure
/// it is rather than folding it into the ordinary empty state.
/// </remarks>
public interface IBootAtlasCatalogue
{
    /// <summary>Reads whatever atlas metadata this installation has.</summary>
    BootAtlasResult Load();
}
