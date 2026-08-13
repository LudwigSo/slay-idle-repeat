using System.Text.Json;

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
    /// <para>
    /// 🔒 <c>assets/</c>, NOT <c>game-data/</c>: <c>LocalFileContentSource</c> enumerates every
    /// <c>*.json</c> under <c>game-data</c> into the <c>ContentSnapshot</c> whose hash M0-09 makes
    /// load-bearing for replay, and a pipeline tuning file has no business moving that stamp.
    /// </para>
    /// <para>
    /// 🔒 <b>Nothing under <c>assets/pipeline/</c> may be a <c>.png</c>, <c>.ogg</c> or <c>.wav</c>.</b>
    /// M8-01a's asset provenance gate walks <c>assets/**</c> recursively as the delivery root and
    /// demands a provenance record for every file it finds with one of those three extensions. A
    /// <c>.json</c> tuning register is invisible to it, which is why this file is safe here, but an
    /// image or a sound dropped beside it would be read as an undocumented delivered asset and fail
    /// the gate. Image output goes to <c>artifacts/</c> (gitignored), never here — nothing this
    /// pipeline writes belongs under a delivery root.
    /// </para>
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

    /// <summary>The one member of a <see cref="ThresholdsPath"/>-shaped file that is not a key.</summary>
    private const string DocMember = "_doc";

    /// <summary>
    /// What asks for each key, so the refusal says which step or check stopped and where to look.
    /// </summary>
    /// <remarks>
    /// 🔒 Seventeen holes can throw the same exception type. "An uncalibrated threshold stopped the
    /// batch" tells a reader nothing about which of the seventeen to go and measure, so the message
    /// carries both the key and its consumer (steering rule S2).
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Consumers =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ThresholdKeys.BackgroundKeyTolerance] = "`15` §B4 step 1 (background removal)",
            [ThresholdKeys.MatteDecontaminationStrength] = "`15` §B4 step 1 (matte decontamination)",
            [ThresholdKeys.OutlineColourTolerance] = "`15` §B4 step 4 and Part F item 3",
            [ThresholdKeys.OutlineGapClosureRadius] = "`15` §B4 step 4 (gap closure)",
            [ThresholdKeys.OutlineWidthUniformityTolerance] = "`15` Part F item 3",
            [ThresholdKeys.PaletteNeutrals] = "`15` §B4 step 3 and Part F item 5",
            [ThresholdKeys.PaletteMatchTolerance] = "`15` §B4 step 3 and Part F item 5",
            [ThresholdKeys.ResizeSharpenRadius] = "`15` §B4 step 5 (unsharp mask radius)",
            [ThresholdKeys.ExportColourBudget] = "`15` §B4 step 6 (the pngquant substitute)",
            [ThresholdKeys.ExportMaxMeanError] = "`15` §B4 step 6 (the pngquant substitute)",
            [ThresholdKeys.HaloMaxFringeRatio] = "`15` Part F item 6",
            [ThresholdKeys.HaloMaxLuminanceDeviation] = "`15` Part F item 6",
            [ThresholdKeys.SilhouetteMinCoverageRatio] = "the `15` §A4 silhouette gate",
            [ThresholdKeys.SilhouetteMinBoundingBoxFill] = "the `15` §A4 silhouette gate",
            [ThresholdKeys.SilhouetteMaxComponentCount] = "the `15` §A4 silhouette gate",
            [ThresholdKeys.SilhouetteMinDistinguishability] = "the `15` §A4 silhouette gate",
            [ThresholdKeys.WatermarkCornerOpacityCeiling] = "`15` Part F item 8's mechanical proxy",
        };

    private readonly IReadOnlyDictionary<string, ThresholdValue?> values;

    private ThresholdSet(IReadOnlyDictionary<string, ThresholdValue?> values) => this.values = values;

    /// <summary>A set in which every key is null — what the shipped file describes.</summary>
    public static ThresholdSet Uncalibrated() =>
        new(Keys.ToDictionary(key => key, _ => (ThresholdValue?)null, StringComparer.Ordinal));

    /// <summary>Reads a threshold file's JSON text.</summary>
    /// <remarks>
    /// A key absent from the JSON, or present with a value of the wrong shape, is a loud failure:
    /// the file is the register of holes, and a register that can silently shrink is not one.
    /// </remarks>
    /// <param name="json">The contents of a <see cref="ThresholdsPath"/>-shaped file.</param>
    public static ThresholdSet LoadFrom(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"A {ThresholdsPath}-shaped file is a JSON object of the {Keys.Count} keys in " +
                $"{nameof(ThresholdSet)}.{nameof(Keys)}; this one is a " +
                $"{document.RootElement.ValueKind}.");
        }

        var read = new Dictionary<string, ThresholdValue?>(StringComparer.Ordinal);
        foreach (var member in document.RootElement.EnumerateObject())
        {
            if (string.Equals(member.Name, DocMember, StringComparison.Ordinal))
            {
                continue;
            }

            if (!Consumers.ContainsKey(member.Name))
            {
                throw new InvalidOperationException(
                    $"'{member.Name}' is not one of the {Keys.Count} thresholds. This file is the " +
                    "register of the holes `15` leaves, so a name nobody consumes is a typo or a " +
                    $"key that was renamed in one place only — see {nameof(ThresholdKeys)}.");
            }

            read[member.Name] = ReadValue(member);
        }

        var missing = Keys.Where(key => !read.ContainsKey(key)).ToArray();
        if (missing.Length > 0)
        {
            throw new InvalidOperationException(
                $"{ThresholdsPath} is missing {missing.Length} of the {Keys.Count} thresholds: " +
                $"{string.Join(", ", missing)}. A hole that is absent from the register is " +
                "invisible rather than greppable, which is the failure steering rule S6 exists to " +
                "prevent — a key nobody has calibrated belongs here with the value null.");
        }

        return new ThresholdSet(read);
    }

    /// <summary>True when the key carries a stated value.</summary>
    /// <param name="key">A <see cref="Keys"/> member. An unknown key is a loud failure.</param>
    public bool IsCalibrated(string key) => Stated(key) is not null;

    /// <summary>
    /// The stated value, or <see cref="UncalibratedThresholdException"/> naming the key.
    /// </summary>
    /// <param name="key">A <see cref="Keys"/> member. An unknown key is a loud failure.</param>
    public ThresholdValue Require(string key) =>
        Stated(key) ?? throw new UncalibratedThresholdException(key, Consumers[key]);

    /// <summary>The stated value as a number.</summary>
    /// <param name="key">A <see cref="Keys"/> member holding a numeric value.</param>
    public double RequireNumber(string key) => Require(key) is NumericThreshold numeric
        ? numeric.Value
        : throw new InvalidOperationException(
            $"Threshold '{key}' holds a colour list, and {Consumers[key]} reads it as a number.");

    /// <summary>The stated value as a colour list.</summary>
    /// <param name="key">A <see cref="Keys"/> member holding a colour list.</param>
    public IReadOnlyList<string> RequireColours(string key) =>
        Require(key) is ColourListThreshold colours
            ? colours.Colours
            : throw new InvalidOperationException(
                $"Threshold '{key}' holds a number, and {Consumers[key]} reads it as a colour list.");

    /// <summary>This set with one numeric value stated by the caller.</summary>
    /// <param name="key">A <see cref="Keys"/> member.</param>
    /// <param name="value">The caller's stated value.</param>
    public ThresholdSet With(string key, double value) => WithValue(key, new NumericThreshold(value));

    /// <summary>This set with one colour list stated by the caller.</summary>
    /// <param name="key">A <see cref="Keys"/> member.</param>
    /// <param name="colours">The caller's stated colours.</param>
    public ThresholdSet WithColours(string key, IReadOnlyList<string> colours)
    {
        ArgumentNullException.ThrowIfNull(colours);
        return WithValue(key, new ColourListThreshold([.. colours]));
    }

    /// <summary>Reads one member of a threshold file, or refuses its shape.</summary>
    /// <param name="member">The JSON member.</param>
    private static ThresholdValue? ReadValue(JsonProperty member) => member.Value.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.Number => new NumericThreshold(member.Value.GetDouble()),
        JsonValueKind.Array => new ColourListThreshold(
        [
            .. member.Value.EnumerateArray().Select(entry =>
                entry.ValueKind == JsonValueKind.String
                    ? entry.GetString()!
                    : throw new InvalidOperationException(
                        $"Threshold '{member.Name}' is a list of hex colours and holds a " +
                        $"{entry.ValueKind}.")),
        ]),
        _ => throw new InvalidOperationException(
            $"Threshold '{member.Name}' is a {member.Value.ValueKind}. A threshold is null, a " +
            "number, or — for the one `15` §A5 leaves unenumerated — a list of hex colours."),
    };

    private ThresholdValue? Stated(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        return values.TryGetValue(key, out var value)
            ? value
            : throw new ArgumentException(
                $"'{key}' is not one of the {Keys.Count} thresholds in " +
                $"{nameof(ThresholdSet)}.{nameof(Keys)}.",
                nameof(key));
    }

    private ThresholdSet WithValue(string key, ThresholdValue value)
    {
        _ = Stated(key);

        var stated = new Dictionary<string, ThresholdValue?>(values, StringComparer.Ordinal)
        {
            [key] = value,
        };

        return new ThresholdSet(stated);
    }
}
