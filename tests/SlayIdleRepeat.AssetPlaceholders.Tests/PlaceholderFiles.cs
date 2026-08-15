using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline;

namespace SlayIdleRepeat.AssetPlaceholders.Tests;

/// <summary>The two committed files this suite reads: the shipped register, and the threshold file.</summary>
/// <remarks>
/// The repository-root walk is re-implemented rather than shared with the other test suites' own
/// copies, so the suites stay independent of each other.
/// </remarks>
internal static class PlaceholderFiles
{
    private const string SolutionFileName = "SlayIdleRepeat.sln";

    private static readonly Lazy<string> LazyRoot = new(FindRepositoryRoot);

    private static readonly Lazy<AssetManifestSet> LazyShipped =
        new(() => AssetManifestReader.Load(Path.Combine(RepositoryRoot, "game-data")));

    /// <summary>The repository root.</summary>
    internal static string RepositoryRoot => LazyRoot.Value;

    /// <summary>The shipped register, loaded once through its own reader and no other way.</summary>
    internal static AssetManifestSet Shipped => LazyShipped.Value;

    /// <summary>The shipped threshold file's raw JSON, every key null.</summary>
    internal static string ThresholdsJson() =>
        File.ReadAllText(Path.Combine(RepositoryRoot, ThresholdSet.ThresholdsPath));

    /// <summary>
    /// A scratch output directory under <c>artifacts/</c>, unique per caller, deleted on dispose.
    /// </summary>
    /// <remarks>
    /// Deliberately under <c>artifacts/</c> rather than the system temp directory, since
    /// <see cref="PlaceholderOutput.RequireArtifactsPath"/> refuses anything else.
    /// </remarks>
    /// <param name="name">A name for this run's directory.</param>
    internal static ScratchDirectory Scratch(string name) => new(Path.Combine(
        RepositoryRoot, PlaceholderOutput.ArtifactsDirectory, "placeholder-tests", name));

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
                "These cases read the real game-data/assets and the real assets/pipeline.");
    }
}

/// <summary>A directory under <c>artifacts/</c> that removes itself.</summary>
/// <param name="path">The directory's absolute path.</param>
internal sealed class ScratchDirectory(string path) : IDisposable
{
    /// <summary>The directory's absolute path.</summary>
    internal string Path { get; } = path;

    /// <summary>Removes the directory and everything in it.</summary>
    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}

/// <summary>
/// The specific shipped rows these cases are written against, each fetched by id so that a case
/// which depends on a row's shape can first assert the shape is really there.
/// </summary>
/// <remarks>
/// The ten generatable rows below are chosen to cover every delivery size present in the register,
/// both pivots, biome-scoped and not, atlased and not — a sample of ten 128×128 centred icons would
/// exercise one code path ten times.
/// </remarks>
internal static class SampleRows
{
    /// <summary>96×96, <c>center</c>, <c>atlas_ui</c>, no biome.</summary>
    internal const string CurrencyIcon = "icon_cur_crown";

    /// <summary>128×128, <c>center</c>, <c>atlas_icons_perks</c>, no biome.</summary>
    internal const string PerkIcon = "icon_perk_aegis";

    /// <summary>192×192, <c>center</c>, <c>atlas_icons_gear</c>, no biome.</summary>
    internal const string GearIcon = "gear_amulet_charm_a";

    /// <summary>192×192, <c>center</c>, and <b>no atlas</b> — the register assigns this row none.</summary>
    internal const string TileIcon = "tile_icon_boss";

    /// <summary>256×256, <c>bottom-center</c>, <c>atlas_pets</c>, no biome.</summary>
    internal const string Pet = "pet_aegisowl_ability_cast";

    /// <summary>512×384 — the non-square delivery the CON_DELIVERY_ASPECT ruling is about.</summary>
    internal const string Mount = "mnt_cindermane_idle";

    /// <summary>512×512, <c>bottom-center</c>, <c>atlas_hero</c>, no biome.</summary>
    internal const string HeroGear = "chr_hero_armor_leathers_a";

    /// <summary>512×512, <c>bottom-center</c>, biome-scoped.</summary>
    internal const string BiomeEnemy = "chr_enemy_astral_brute_idle";

    /// <summary>640×640, <c>bottom-center</c>, biome-scoped.</summary>
    internal const string BiomeElite = "chr_elite_ashwing_attack";

    /// <summary>1024×1024 — the one size that takes the 2048 upscaled generation canvas.</summary>
    internal const string BiomeBoss = "chr_boss_cindermaw_attack";

    /// <summary>A VFX sheet, cut by ruling O8.</summary>
    internal const string CutRow = "vfx_bleed_loop_sheet";

    /// <summary>A background: the register states no delivery size for it, and no pivot either.</summary>
    internal const string RowWithoutDeliverySize = "bg_arena";

    /// <summary>A board decor prop: it HAS a delivery size but no stated pivot.</summary>
    internal const string RowWithoutPivot = "board_astral_decor_1";

    /// <summary>The ten generatable rows the batch cases run over, in register id order.</summary>
    internal static IReadOnlyList<string> Generatable { get; } =
    [
        BiomeBoss, BiomeElite, BiomeEnemy, HeroGear, CurrencyIcon, PerkIcon, GearIcon, Pet, Mount,
        TileIcon,
    ];

    /// <summary>The three rows that must be skipped, and the reason each must be skipped for.</summary>
    internal static IReadOnlyDictionary<string, PlaceholderSkipReason> Skippable { get; } =
        new Dictionary<string, PlaceholderSkipReason>(StringComparer.Ordinal)
        {
            [CutRow] = PlaceholderSkipReason.CutByRuling,
            [RowWithoutDeliverySize] = PlaceholderSkipReason.NoDeliverySize,
            [RowWithoutPivot] = PlaceholderSkipReason.NoPivot,
        };

    /// <summary>One row from the shipped register, or a loud failure naming the id.</summary>
    /// <param name="id">An asset id.</param>
    internal static ArtAsset Require(string id) => PlaceholderFiles.Shipped.RequireArt(id);
}
