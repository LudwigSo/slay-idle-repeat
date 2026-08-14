using SlayIdleRepeat.AssetManifest;
using SlayIdleRepeat.AssetPipeline;

namespace SlayIdleRepeat.AssetPlaceholders.Tests;

/// <summary>
/// The two committed files this suite reads: M8-09's shipped register, and the S6 threshold file.
/// </summary>
/// <remarks>
/// <para>
/// The repository-root walk is the same one <c>SlayIdleRepeat.AssetPipeline.Tests/PipelineFiles.cs</c>
/// and <c>SlayIdleRepeat.AssetManifest.Tests/ManifestFiles.cs</c> do — climb from
/// <see cref="AppContext.BaseDirectory"/> until the solution file appears. It is re-implemented
/// rather than shared for the reason those two give: the suites must not depend on each other, and
/// a linked file would drag each one's fixtures into the others' scope.
/// </para>
/// <para>
/// 🔒 Reading committed text files is not an integration test. Both are checked into this
/// repository; nothing here starts a container, a server, a database or the Godot runtime, and no
/// native tool is invoked.
/// </para>
/// </remarks>
internal static class PlaceholderFiles
{
    private const string SolutionFileName = "SlayIdleRepeat.sln";

    private static readonly Lazy<string> LazyRoot = new(FindRepositoryRoot);

    private static readonly Lazy<AssetManifestSet> LazyShipped =
        new(() => AssetManifestReader.Load(Path.Combine(RepositoryRoot, "game-data")));

    /// <summary>The repository root.</summary>
    internal static string RepositoryRoot => LazyRoot.Value;

    /// <summary>M8-09's shipped register, loaded once through M8-09's reader and no other way.</summary>
    internal static AssetManifestSet Shipped => LazyShipped.Value;

    /// <summary>The shipped threshold file's raw JSON, every key null.</summary>
    internal static string ThresholdsJson() =>
        File.ReadAllText(Path.Combine(RepositoryRoot, ThresholdSet.ThresholdsPath));

    /// <summary>
    /// A scratch output directory under <c>artifacts/</c>, unique per caller, deleted on dispose.
    /// </summary>
    /// <remarks>
    /// 🔒 Under <c>artifacts/</c> and not under the system temp directory, deliberately.
    /// <see cref="PlaceholderOutput.RequireArtifactsPath"/> refuses anything else, and a suite that
    /// wrote to a temp path would have to bypass the guard it is meant to be exercising.
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
/// 🔒 Every id and every shape here was measured against the committed manifest, not assumed. The
/// ten generatable rows below are chosen to cover all eight `15` §C delivery sizes present in the
/// register, both §C pivots, biome-scoped and not, and atlased and not — because a sample that
/// happened to be ten 128×128 centred icons would exercise one code path ten times.
/// </remarks>
internal static class SampleRows
{
    /// <summary>96×96, <c>center</c>, <c>atlas_ui</c>, no biome.</summary>
    internal const string CurrencyIcon = "icon_cur_crown";

    /// <summary>128×128, <c>center</c>, <c>atlas_icons_perks</c>, no biome.</summary>
    internal const string PerkIcon = "icon_perk_aegis";

    /// <summary>192×192, <c>center</c>, <c>atlas_icons_gear</c>, no biome.</summary>
    internal const string GearIcon = "gear_amulet_charm_a";

    /// <summary>192×192, <c>center</c>, and 🔒 <b>no atlas</b> — `15` §D2 assigns this row none.</summary>
    internal const string TileIcon = "tile_icon_boss";

    /// <summary>256×256, <c>bottom-center</c>, <c>atlas_pets</c>, no biome.</summary>
    internal const string Pet = "pet_aegisowl_ability_cast";

    /// <summary>512×384 — 🔒 the non-square delivery the CON_DELIVERY_ASPECT ruling is about.</summary>
    internal const string Mount = "mnt_cindermane_idle";

    /// <summary>512×512, <c>bottom-center</c>, <c>atlas_hero</c>, no biome.</summary>
    internal const string HeroGear = "chr_hero_armor_leathers_a";

    /// <summary>512×512, <c>bottom-center</c>, biome-scoped — `15` §B4 step 3 applies.</summary>
    internal const string BiomeEnemy = "chr_enemy_astral_brute_idle";

    /// <summary>640×640, <c>bottom-center</c>, biome-scoped.</summary>
    internal const string BiomeElite = "chr_elite_ashwing_attack";

    /// <summary>1024×1024 — 🔒 the one size that takes `15` §C's 2048 generation canvas.</summary>
    internal const string BiomeBoss = "chr_boss_cindermaw_attack";

    /// <summary>A `15` §E19 VFX sheet, cut by ruling O8.</summary>
    internal const string CutRow = "vfx_bleed_loop_sheet";

    /// <summary>A background: `15` §C states no delivery size for it, and no pivot either.</summary>
    internal const string RowWithoutDeliverySize = "bg_arena";

    /// <summary>A board decor prop: it HAS a delivery size and `15` §C states no pivot for it.</summary>
    internal const string RowWithoutPivot = "board_astral_decor_1";

    /// <summary>
    /// The ten generatable rows the batch cases run over, in `15` §D1 id order.
    /// </summary>
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
    /// <param name="id">A `15` §D1 asset id.</param>
    internal static ArtAsset Require(string id) => PlaceholderFiles.Shipped.RequireArt(id);
}
