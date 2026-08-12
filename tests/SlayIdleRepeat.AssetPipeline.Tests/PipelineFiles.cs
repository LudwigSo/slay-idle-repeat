using SlayIdleRepeat.AssetManifest;

namespace SlayIdleRepeat.AssetPipeline.Tests;

/// <summary>
/// The two committed files this suite reads: M8-09's shipped register, and the S6 threshold file.
/// </summary>
/// <remarks>
/// <para>
/// The repository-root walk is the same one <c>SlayIdleRepeat.AssetManifest.Tests/ManifestFiles.cs</c>
/// does — climb from <see cref="AppContext.BaseDirectory"/> until the solution file appears. It is
/// re-implemented rather than shared on purpose: the two suites must not depend on each other, and
/// a linked file would put M8-09's mutation helpers (which this suite has no use for) in scope here.
/// </para>
/// <para>
/// 🔒 Reading committed text files is not an integration test. Both are checked into this
/// repository; nothing here starts a container, a server, a database or the Godot runtime.
/// </para>
/// </remarks>
internal static class PipelineFiles
{
    private const string SolutionFileName = "SlayIdleRepeat.sln";

    private static readonly Lazy<string> LazyRoot = new(FindRepositoryRoot);

    private static readonly Lazy<AssetManifestSet> LazyShipped =
        new(() => AssetManifestReader.Load(Path.Combine(RepositoryRoot, "game-data")));

    /// <summary>The repository root.</summary>
    internal static string RepositoryRoot => LazyRoot.Value;

    /// <summary>M8-09's shipped register, loaded once through M8-09's reader and no other way.</summary>
    internal static AssetManifestSet Shipped => LazyShipped.Value;

    /// <summary>The absolute path of the shipped threshold file.</summary>
    internal static string ThresholdsFile =>
        Path.Combine(RepositoryRoot, ThresholdSet.ThresholdsPath);

    /// <summary>The shipped threshold file's raw JSON.</summary>
    internal static string ThresholdsJson() => File.ReadAllText(ThresholdsFile);

    /// <summary>The heading `15` Part F's checklist lives under.</summary>
    private const string PartFHeading = "# PART F — QUALITY ASSURANCE CHECKLIST";

    /// <summary>The Markdown task-list marker every Part F item is written as.</summary>
    private const string TaskListMarker = "- [ ] ";

    /// <summary>The absolute path of `15`, the only design doc this suite reads.</summary>
    internal static string Doc15File => Path.Combine(
        RepositoryRoot, "game-design", "15_ART_DIRECTION_AND_ASSET_MANIFEST.md");

    /// <summary>
    /// `15` Part F's checklist lines, read out of the committed doc with their Markdown removed:
    /// the leading task-list marker, and the code fence item 3 wraps its colour in.
    /// </summary>
    /// <remarks>
    /// 🔒 Nothing else is normalised. The em dash in item 6, the en dash in item 9 and the section
    /// signs in items 7 and 10 come through as the doc's own bytes, so that
    /// <c>QaChecklistTests</c>'s ordinal comparison catches a constant that retyped one as an ASCII
    /// hyphen — which is the way "verbatim" actually rots.
    /// </remarks>
    internal static IReadOnlyList<string> Doc15PartFLines()
    {
        var lines = File.ReadAllLines(Doc15File);
        var start = Array.FindIndex(
            lines, line => line.StartsWith(PartFHeading, StringComparison.Ordinal));

        if (start < 0)
        {
            throw new InvalidOperationException(
                $"'{PartFHeading}' is not in {Doc15File}. The QA checklist's text is reconciled " +
                "against that heading, so a renamed heading must fail loudly rather than silently " +
                "reconcile against nothing.");
        }

        return
        [
            .. lines
                .Skip(start)
                .TakeWhile(line => !line.StartsWith("# PART G", StringComparison.Ordinal))
                .Where(line => line.StartsWith(TaskListMarker, StringComparison.Ordinal))
                .Select(line => line[TaskListMarker.Length..].Replace("`", string.Empty, StringComparison.Ordinal)),
        ];
    }

    /// <summary>The production source directory of <c>SlayIdleRepeat.AssetPipeline</c>.</summary>
    internal static string ProductionSourceDirectory =>
        Path.Combine(RepositoryRoot, "tools", "AssetPipeline");

    /// <summary>
    /// Every hand-written <c>.cs</c> file of the production project — <c>obj/</c> and <c>bin/</c>
    /// excluded, because generated assembly-info files are not something an author wrote.
    /// </summary>
    internal static IReadOnlyList<string> ProductionSourceFiles() =>
    [
        .. Directory
            .EnumerateFiles(ProductionSourceDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal),
    ];

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

/// <summary>
/// The specific shipped rows these cases are written against, each fetched by id so that a case
/// which depends on a row's shape can first assert that the shape is really there.
/// </summary>
/// <remarks>
/// 🔒 Every id here was measured against the committed manifest, not assumed. A case that needs a
/// row with a hole in it asserts the hole exists before exercising the hole — otherwise the case
/// would pass the day somebody fills it in, which is the day it should fail.
/// </remarks>
internal static class ManifestRows
{
    /// <summary>A currency icon: <c>atlas_ui</c>, 96x96, <c>center</c>, and NO biome.</summary>
    internal const string NonBiomeUiIcon = "icon_cur_crown";

    /// <summary>A second currency icon, for the atlas packer's multi-member cases.</summary>
    internal const string NonBiomeUiIconSecond = "icon_cur_energy";

    /// <summary>A third currency icon, for the atlas packer's multi-member cases.</summary>
    internal const string NonBiomeUiIconThird = "icon_cur_beast_feed";

    /// <summary>An astral enemy: biome-scoped, palette carried, 512x512, <c>bottom-center</c>.</summary>
    internal const string BiomeScopedCharacter = "chr_enemy_astral_brute_idle";

    /// <summary>A `15` §E19 VFX sheet — cut by ruling O8, "procedural in-engine".</summary>
    internal const string CutVfxSheet = "vfx_bleed_loop_sheet";

    /// <summary>A background: `15` §C states no delivery size for it (DSC_MISSING_SIZES).</summary>
    internal const string RowWithoutDeliverySize = "bg_arena";

    /// <summary>A board decor prop: it HAS a delivery size and `15` §C states no pivot for it.</summary>
    internal const string RowWithoutPivot = "board_astral_decor_1";

    /// <summary>The `15` §E19 section id — every row in it is cut.</summary>
    internal const string VfxSection = "E19";

    /// <summary>One row from the shipped register, or a loud failure naming the id.</summary>
    /// <param name="id">A `15` §D1 asset id.</param>
    internal static ArtAsset Require(string id) => PipelineFiles.Shipped.RequireArt(id);
}
