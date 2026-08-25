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

    /// <summary>The Markdown task-list marker every Part F item is written as.</summary>
    private const string TaskListMarker = "- [ ] ";

    /// <summary>The heading Part F opens with. Renaming it must fail loudly, not reconcile nothing.</summary>
    private const string PartFHeading = "# PART F \u2014 QUALITY ASSURANCE CHECKLIST";

    /// <summary>The heading Part F ends at.</summary>
    private const string PartGHeading = "# PART G";

    /// <summary>The absolute path of `15`, the only design doc this suite reads.</summary>
    internal static string Doc15File => Path.Combine(
        RepositoryRoot, "game-design", "15_ART_DIRECTION_AND_ASSET_MANIFEST.md");

    /// <summary>
    /// `15` Part F's checklist lines, read out of the committed doc with their Markdown removed:
    /// the leading task-list marker, and the backticks around inline code.
    /// </summary>
    /// <remarks>
    /// \U0001F512 Nothing else is normalised. Em dashes, en dashes, section signs and the emphasis
    /// asterisks inside a kind tag come through as the doc's own bytes, so that
    /// <c>QaChecklistTests</c>'s ordinal comparison catches a constant that retyped one as ASCII
    /// \u2014 which is the way "verbatim" actually rots.
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
                .TakeWhile(line => !line.StartsWith(PartGHeading, StringComparison.Ordinal))
                .Where(line => line.StartsWith(TaskListMarker, StringComparison.Ordinal))
                .Select(line => line[TaskListMarker.Length..].Replace("`", string.Empty, StringComparison.Ordinal)),
        ];
    }

    /// <summary>
    /// `15` \u00a7A4's acceptance sentence, read out of the committed doc with its emphasis removed.
    /// </summary>
    /// <remarks>
    /// \U0001F534 This reader exists because its absence was a real defect. \u00a7A4's sentence changed from
    /// "regenerate it" to "remodel it" with D60 and <c>Doc15PartF.SilhouetteAcceptanceSentence</c>
    /// kept the old wording, green, for two days \u2014 the sentence was only ever compared against an
    /// internally-composed string, so nothing reconciled it against the doc (`16` D60 consequence
    /// 5b). Part F had a tripwire and this did not.
    /// </remarks>
    internal static string Doc15SilhouetteAcceptanceSentence()
    {
        const string opening = "If you cannot tell which character it is,";

        var line = Array.Find(
            File.ReadAllLines(Doc15File),
            candidate => candidate.Contains(opening, StringComparison.Ordinal));

        if (line is null)
        {
            throw new InvalidOperationException(
                $"No line of {Doc15File} contains '{opening}'. \u00a7A4's acceptance sentence is " +
                "reconciled against the doc, so a reworded opening must fail loudly rather than " +
                "quietly measure nothing.");
        }

        var plain = line.Replace("**", string.Empty, StringComparison.Ordinal);
        var from = plain.IndexOf(opening, StringComparison.Ordinal);
        var to = plain.IndexOf('.', from);

        return plain[from..(to + 1)];
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

    /// <summary>
    /// A hero gear row delivering at `15` §C's canonical 512x512, <c>bottom-center</c>, and NO
    /// biome — the composed-run case's asset, so `15` §B4 step 3 skips and the run is about the
    /// step 2 / step 5 division of labour and nothing else.
    /// </summary>
    internal const string SquareCharacterDeliveringAt512 = "chr_hero_armor_leathers_a";

    /// <summary>
    /// A mount: 512x384, so `15` §C's square generation canvas cannot reach it without distorting.
    /// Non-biome, so `15` §B4 step 3 skips on it too.
    /// </summary>
    internal const string NonSquareDeliveryMount = "mnt_cindermane_idle";

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
