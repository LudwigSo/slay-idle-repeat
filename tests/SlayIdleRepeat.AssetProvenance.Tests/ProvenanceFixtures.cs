using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetProvenance.Tests;

/// <summary>
/// The real repository, the real register and the real store, plus the builders the synthetic
/// cases drive the gate with.
/// </summary>
/// <remarks>
/// The gate cases run against the <b>real</b> shipped register rather than a hand-built one, so a
/// case that passes is a case about this project rather than about a fixture that agrees with
/// itself. What the cases synthesise is the part that does not exist yet: the delivery set and the
/// records.
/// </remarks>
internal static class ProvenanceFixtures
{
    private const string SolutionFileName = "SlayIdleRepeat.sln";

    private static readonly Lazy<string> LazyRoot = new(FindRepositoryRoot);
    private static readonly Lazy<AssetManifestSet> LazyRegister =
        new(() => AssetManifestReader.Load(DataRoot));
    private static readonly Lazy<ProvenanceRecordSet> LazyStore =
        new(() => ProvenanceStore.Load(StoreRoot));

    /// <summary>The repository root.</summary>
    internal static string RepositoryRoot => LazyRoot.Value;

    /// <summary>The <c>game-data</c> root.</summary>
    internal static string DataRoot => Path.Combine(RepositoryRoot, "game-data");

    /// <summary>The committed provenance store.</summary>
    internal static string StoreRoot => ProvenanceStore.RootFor(RepositoryRoot);

    /// <summary>The committed delivery root.</summary>
    internal static string DeliveryRoot => DeliveredAssets.RootFor(RepositoryRoot);

    /// <summary>The shipped register.</summary>
    internal static AssetManifestSet Register => LazyRegister.Value;

    /// <summary>The shipped store, as committed.</summary>
    internal static ProvenanceRecordSet ShippedStore => LazyStore.Value;

    // ---------------------------------------------------------------- known real ids
    //
    // Named constants rather than string literals scattered through the cases, so that a rename in
    // the register breaks compilation in one place instead of turning five cases green over ids
    // that no longer exist.

    /// <summary>A real, uncut ART id.</summary>
    internal const string ArtId = "chr_hero_body_idle";

    /// <summary>A second real, uncut ART id.</summary>
    internal const string OtherArtId = "ui_panel_main_9slice";

    /// <summary>A real AUDIO id.</summary>
    internal const string AudioId = "mus_home";

    /// <summary>A real ART id the O8 ruling CUT.</summary>
    internal const string CutArtId = "vfx_bleed_loop_sheet";

    /// <summary>An id in neither register.</summary>
    internal const string UnknownId = "chr_hero_body_idle_but_renamed";

    /// <summary>The one tool <c>tool-licences.json</c> declares.</summary>
    internal const string DeclaredTool = "midjourney";

    /// <summary>A full 40-hex commit, for procedural records.</summary>
    internal const string SomeCommit = "8f9267b0000000000000000000000000000000ab";

    // ---------------------------------------------------------------- record builders

    /// <summary>A well-formed Midjourney record for an art id.</summary>
    internal static MidjourneyProvenance Midjourney(string assetId = ArtId) => new()
    {
        AssetId = assetId,
        Tooling = null,
        JobId = "9f0e3a12-7c44-4c1e-9a6f-2b1d0c5e8a77",
        Prompt = "chibi cartoon fantasy game art, 2.5 heads tall proportions",
        Seed = "1477201933",
        Sref = "3821991",
        AspectRatio = "1:1",
        Style = "raw",
        Stylize = "250",
        ModelVersion = "v6.1",
        Date = "2026-09-01",
    };

    /// <summary>A well-formed procedural record for an art id.</summary>
    internal static ProceduralProvenance Procedural(string assetId = OtherArtId) => new()
    {
        AssetId = assetId,
        Tooling = null,
        Generator = "SlayIdleRepeat.PlaceholderGenerator",
        RepoCommit = SomeCommit,
        Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["width"] = "512",
            ["height"] = "512",
        },
    };

    /// <summary>A well-formed CC0 record for an audio id.</summary>
    internal static Cc0Provenance Cc0(string assetId = AudioId) => new()
    {
        AssetId = assetId,
        Tooling = new AudioTooling("Audacity", "3.5.1"),
        Source = "Kenney UI Audio",
        Url = "https://kenney.nl/assets/ui-audio",
        Licence = "CC0-1.0",
        DateRetrieved = "2026-09-01",
    };

    // ---------------------------------------------------------------- set builders

    /// <summary>A record set over the given records, with the shipped licence register.</summary>
    internal static ProvenanceRecordSet StoreOf(params ProvenanceRecord[] records) =>
        new(records, ShippedStore.Licences);

    /// <summary>A record set over the given records and a bespoke licence register.</summary>
    internal static ProvenanceRecordSet StoreOf(
        IReadOnlyList<ProvenanceRecord> records, IReadOnlyList<ToolLicence> licences) =>
        new(records, new ToolLicenceRegister(licences));

    /// <summary>A delivery of one art asset, at the path a pipeline would export it to.</summary>
    internal static DeliveredAsset DeliveredArt(string assetId = ArtId) =>
        new(assetId, $"art/{assetId}.png", ".png");

    /// <summary>A delivery of one audio asset.</summary>
    internal static DeliveredAsset DeliveredAudio(string assetId = AudioId) =>
        new(assetId, $"audio/{assetId}.ogg", ".ogg");

    /// <summary>A licence entry that is confirmed in writing, for the cases that need one.</summary>
    internal static ToolLicence ConfirmedLicence(string tool = DeclaredTool) =>
        new(tool, "art", true, "docs/legal/midjourney-terms-2026-09-01.pdf", "confirmed for this case");

    /// <summary>The shipped licence register's entries, as a list.</summary>
    internal static IReadOnlyList<ToolLicence> ShippedLicences => ShippedStore.Licences.Licences;

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException(
                $"Could not find {SolutionFileName} in any ancestor of {AppContext.BaseDirectory}. " +
                "These cases read the real game-data/assets register and the real assets/provenance store.");
    }
}

/// <summary>A throwaway directory tree, deleted when the case finishes.</summary>
internal sealed class TempTree : IDisposable
{
    internal TempTree()
    {
        Root = Path.Combine(Path.GetTempPath(), "sir-provenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>The tree's root.</summary>
    internal string Root { get; }

    /// <summary>Creates a file, and every directory above it.</summary>
    internal string Write(string relativePath, string content = "")
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Creates a directory.</summary>
    internal string Directory_(string relativePath)
    {
        var path = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>Builds a store directory holding the given records, plus a licence register.</summary>
    internal string WriteStore(
        IReadOnlyList<ProvenanceRecord> records, string? licencesJson = null)
    {
        var store = Directory_("provenance");
        Directory_("provenance/records");
        File.WriteAllText(
            Path.Combine(store, ProvenanceStore.LicenceFileName),
            licencesJson ?? File.ReadAllText(
                Path.Combine(ProvenanceFixtures.StoreRoot, ProvenanceStore.LicenceFileName)));

        foreach (var record in records)
        {
            Write(
                $"provenance/records/{record.AssetId}{ProvenanceStore.RecordExtension}",
                ProvenanceStore.WriteRecord(record));
        }

        return store;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leaked temp directory is not worth failing a case over.
        }
    }
}
