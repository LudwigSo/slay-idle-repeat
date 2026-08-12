namespace SlayIdleRepeat.AssetPipeline;

/// <summary>
/// A calibrated threshold value. Numeric for sixteen of the seventeen keys; a colour list for
/// <see cref="ThresholdKeys.PaletteNeutrals"/>, which `15` §A5 names ("+ neutrals") and never
/// enumerates.
/// </summary>
public abstract record ThresholdValue
{
    private protected ThresholdValue()
    {
    }
}

/// <summary>A threshold calibrated to a number.</summary>
/// <param name="Value">The stated value.</param>
public sealed record NumericThreshold(double Value) : ThresholdValue;

/// <summary>A threshold calibrated to a list of hex colours.</summary>
/// <param name="Colours">The stated colours, in the order the caller stated them.</param>
public sealed record ColourListThreshold(IReadOnlyList<string> Colours) : ThresholdValue;

/// <summary>
/// The greppable register of every number `15` does <b>not</b> authorise.
/// </summary>
/// <remarks>
/// 🔒 One name per hole. A step or a QA check asks for a key by one of these constants, so
/// <c>grep</c> over this file answers "what has nobody calibrated yet?" in one pass.
/// </remarks>
public static class ThresholdKeys
{
    /// <summary>Step 1 — how close to the sampled border colour counts as background.</summary>
    public const string BackgroundKeyTolerance = "backgroundKeyTolerance";

    /// <summary>Step 1 — how hard to un-mix the background colour out of edge pixels.</summary>
    public const string MatteDecontaminationStrength = "matteDecontaminationStrength";

    /// <summary>Step 4, QA 3 — how far from `#231A2E` still reads as outline (antialiasing).</summary>
    public const string OutlineColourTolerance = "outlineColourTolerance";

    /// <summary>Step 4 — the largest gap treated as a break to close, not a design feature.</summary>
    public const string OutlineGapClosureRadius = "outlineGapClosureRadius";

    /// <summary>QA 3 — permitted spread around the `15` §A3 3-4 px band.</summary>
    public const string OutlineWidthUniformityTolerance = "outlineWidthUniformityTolerance";

    /// <summary>Step 3, QA 5 — `15` §A5 says "+ neutrals" and never enumerates them.</summary>
    public const string PaletteNeutrals = "paletteNeutrals";

    /// <summary>Step 3, QA 5 — per-pixel distance at which a colour counts as on-palette.</summary>
    public const string PaletteMatchTolerance = "paletteMatchTolerance";

    /// <summary>Step 5 — §B4 gives the sharpen amount (0.4) but no radius/sigma.</summary>
    public const string ResizeSharpenRadius = "resizeSharpenRadius";

    /// <summary>Step 6 — the pngquant substitute's palette size.</summary>
    public const string ExportColourBudget = "exportColourBudget";

    /// <summary>Step 6 — the accept/reject floor standing in for pngquant's quality 80.</summary>
    public const string ExportMaxMeanError = "exportMaxMeanError";

    /// <summary>QA 6 — permitted semi-transparent fringe.</summary>
    public const string HaloMaxFringeRatio = "haloMaxFringeRatio";

    /// <summary>QA 6 — how white or black a fringe pixel may be.</summary>
    public const string HaloMaxLuminanceDeviation = "haloMaxLuminanceDeviation";

    /// <summary>`15` §A4 gate — minimum share of the frame the silhouette must occupy.</summary>
    public const string SilhouetteMinCoverageRatio = "silhouetteMinCoverageRatio";

    /// <summary>`15` §A4 gate — minimum share of its own bounding box the silhouette must fill.</summary>
    public const string SilhouetteMinBoundingBoxFill = "silhouetteMinBoundingBoxFill";

    /// <summary>`15` §A4 gate — maximum number of 8-connected components.</summary>
    public const string SilhouetteMaxComponentCount = "silhouetteMaxComponentCount";

    /// <summary>`15` §A4 gate — minimum distance from an already-accepted silhouette.</summary>
    public const string SilhouetteMinDistinguishability = "silhouetteMinDistinguishability";

    /// <summary>QA 8 mechanical proxy — corner opacity above which a signature is suspected.</summary>
    public const string WatermarkCornerOpacityCeiling = "watermarkCornerOpacityCeiling";
}

/// <summary>
/// Asked for a threshold `15` does not authorise and nobody has calibrated.
/// </summary>
/// <remarks>
/// 🔒 Steering rule S6: <em>"Never coerce such a hole to a default at read time; fail loudly."</em>
/// The message names the key so a failing run says which hole stopped it, not merely that one did.
/// </remarks>
public sealed class UncalibratedThresholdException : InvalidOperationException
{
    /// <summary>Creates the exception for one key.</summary>
    /// <param name="key">The <see cref="ThresholdKeys"/> constant that is null.</param>
    /// <param name="neededBy">What asked for it, e.g. "15 §B4 step 1".</param>
    public UncalibratedThresholdException(string key, string neededBy)
        : base($"Threshold '{key}' is uncalibrated: {neededBy} needs it and `15` authorises no " +
               $"value for it. It ships as null in {ThresholdSet.ThresholdsPath} on purpose " +
               "(steering rule S6 — never fill a hole with a plausible value). Either calibrate " +
               "it against a real batch, or pass an explicit stated value in code and say why.")
    {
        Key = key;
        NeededBy = neededBy;
    }

    /// <summary>The uncalibrated key.</summary>
    public string Key { get; }

    /// <summary>What asked for it.</summary>
    public string NeededBy { get; }
}

/// <summary>
/// The seventeen uncalibrated thresholds, and the refusal to default any of them.
/// </summary>
/// <remarks>
/// <para>
/// The shipped file at <see cref="ThresholdsPath"/> holds every key with the value <c>null</c>.
/// M8-10 is the first batch that could calibrate them. Until then every read throws
/// <see cref="UncalibratedThresholdException"/> naming the key.
/// </para>
/// <para>
/// 🔒 A caller MAY state a value in code with <see cref="With"/> / <see cref="WithColours"/>. That
/// is a caller's stated decision, recorded at the call site, not a default — the distinction is
/// the whole point, and a test that states one must say in a comment why the value is
/// arbitrary-but-sufficient for the fixture it drives.
/// </para>
/// </remarks>
public sealed class ThresholdSet
{
    /// <summary>Where the shipped register lives, relative to the repository root.</summary>
    /// <remarks>
    /// 🔒 <c>assets/</c>, NOT <c>game-data/</c>: <c>LocalFileContentSource</c> enumerates every
    /// <c>*.json</c> under <c>game-data</c> into the <c>ContentSnapshot</c> whose hash M0-09 makes
    /// load-bearing for replay, and a pipeline tuning file has no business moving that stamp.
    /// </remarks>
    public const string ThresholdsPath = "assets/pipeline/thresholds.json";

    /// <summary>
    /// Every uncalibrated key, in the order the M8-06 handover tabulates them.
    /// </summary>
    /// <remarks>
    /// 🔒 Literal data rather than a read of the shipped file: the file is checked <em>against</em>
    /// this list, so a key silently dropped from the file is a test failure rather than a shorter
    /// list that agrees with itself.
    /// </remarks>
    public static IReadOnlyList<string> Keys { get; } =
    [
        ThresholdKeys.BackgroundKeyTolerance,
        ThresholdKeys.MatteDecontaminationStrength,
        ThresholdKeys.OutlineColourTolerance,
        ThresholdKeys.OutlineGapClosureRadius,
        ThresholdKeys.OutlineWidthUniformityTolerance,
        ThresholdKeys.PaletteNeutrals,
        ThresholdKeys.PaletteMatchTolerance,
        ThresholdKeys.ResizeSharpenRadius,
        ThresholdKeys.ExportColourBudget,
        ThresholdKeys.ExportMaxMeanError,
        ThresholdKeys.HaloMaxFringeRatio,
        ThresholdKeys.HaloMaxLuminanceDeviation,
        ThresholdKeys.SilhouetteMinCoverageRatio,
        ThresholdKeys.SilhouetteMinBoundingBoxFill,
        ThresholdKeys.SilhouetteMaxComponentCount,
        ThresholdKeys.SilhouetteMinDistinguishability,
        ThresholdKeys.WatermarkCornerOpacityCeiling,
    ];

    /// <summary>A set in which every key is null — what the shipped file describes.</summary>
    public static ThresholdSet Uncalibrated() => throw new NotImplementedException();

    /// <summary>Reads a threshold file's JSON text.</summary>
    /// <remarks>
    /// A key absent from the JSON, or present with a value of the wrong shape, is a loud failure:
    /// the file is the register of holes, and a register that can silently shrink is not one.
    /// </remarks>
    /// <param name="json">The contents of a <see cref="ThresholdsPath"/>-shaped file.</param>
    public static ThresholdSet LoadFrom(string json) => throw new NotImplementedException();

    /// <summary>True when the key carries a stated value.</summary>
    /// <param name="key">A <see cref="Keys"/> member. An unknown key is a loud failure.</param>
    public bool IsCalibrated(string key) => throw new NotImplementedException();

    /// <summary>
    /// The stated value, or <see cref="UncalibratedThresholdException"/> naming the key.
    /// </summary>
    /// <param name="key">A <see cref="Keys"/> member. An unknown key is a loud failure.</param>
    public ThresholdValue Require(string key) => throw new NotImplementedException();

    /// <summary>The stated value as a number.</summary>
    /// <param name="key">A <see cref="Keys"/> member holding a numeric value.</param>
    public double RequireNumber(string key) => throw new NotImplementedException();

    /// <summary>The stated value as a colour list.</summary>
    /// <param name="key">A <see cref="Keys"/> member holding a colour list.</param>
    public IReadOnlyList<string> RequireColours(string key) => throw new NotImplementedException();

    /// <summary>This set with one numeric value stated by the caller.</summary>
    /// <param name="key">A <see cref="Keys"/> member.</param>
    /// <param name="value">The caller's stated value.</param>
    public ThresholdSet With(string key, double value) => throw new NotImplementedException();

    /// <summary>This set with one colour list stated by the caller.</summary>
    /// <param name="key">A <see cref="Keys"/> member.</param>
    /// <param name="colours">The caller's stated colours.</param>
    public ThresholdSet WithColours(string key, IReadOnlyList<string> colours) =>
        throw new NotImplementedException();
}
