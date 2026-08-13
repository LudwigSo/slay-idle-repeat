using SlayIdleRepeat.AssetPipeline;

namespace SlayIdleRepeat.AssetPlaceholders;

/// <summary>
/// The <b>processing</b> values this generator states so that `15` §B4's steps can run at all — and
/// the loud refusal to state any of the values `15` Part F grades against.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>Two threshold sets, and the split is the whole point.</b> Nine of M8-06's seventeen holes
/// are inputs a §B4 step cannot run without: step 1 needs a key tolerance and a decontamination
/// strength, step 3 needs a palette tolerance and the neutral list, step 4 needs a colour tolerance
/// and a closure radius, step 5 needs a sharpen radius, step 6 needs a colour budget and an error
/// floor. Reading any of them uncalibrated throws, so a batch could not be produced at all without
/// stating them. <see cref="ThresholdSet"/> sanctions exactly this in writing: <em>"A caller MAY
/// state a value in code with With / WithColours. That is a caller's stated decision, recorded at
/// the call site, not a default."</em> Every value below carries the reasoning that fixed it.
/// </para>
/// <para>
/// 🔒 <b>The Part F gate is handed the shipped register verbatim, every key null</b> — see
/// <see cref="ForQualityAssurance"/>. Nothing here calibrates a QA cutoff. Turning
/// <see cref="AssetPipeline.Qa.QaVerdict.Uncalibrated"/> into
/// <see cref="AssetPipeline.Qa.QaVerdict.Pass"/> by typing a number into a threshold is the exact
/// S6 violation the null register exists to prevent, and a placeholder batch — which is not art and
/// which no human has looked at — is the worst possible evidence to calibrate against.
/// </para>
/// </remarks>
public static class PlaceholderThresholds
{
    /// <summary>
    /// Step 1's key tolerance. <b>Zero: exact match only.</b>
    /// </summary>
    /// <remarks>
    /// This generator writes exact bytes and leaves the border fully transparent, so
    /// <c>BackgroundRemovalStep</c> samples no border colour at all and the key falls back to
    /// "already transparent". A non-zero tolerance would be a number with nothing to do; zero says
    /// truthfully that nothing here needs keying by colour. A real Midjourney batch will need a
    /// measured value, and this is not it.
    /// </remarks>
    public const double BackgroundKeyTolerance = 0d;

    /// <summary>
    /// Step 1's decontamination strength. <b>One: the maximum.</b>
    /// </summary>
    /// <remarks>
    /// Stated at full strength on purpose. The outline band is several pixels thick and uniformly
    /// <c>#231A2E</c>, so both fringe layers reconstruct to the colour they already carry and the
    /// step is a no-op whatever the strength — which means a low value would look like caution while
    /// buying nothing, and the maximum exercises the step's arithmetic at its most aggressive.
    /// </remarks>
    public const double MatteDecontaminationStrength = 1d;

    /// <summary>
    /// Step 4's outline-colour tolerance. <b>90 channel units, fixed by arithmetic.</b>
    /// </summary>
    /// <remarks>
    /// <para>
    /// It has to sit above every antialiased outline pixel this generator draws and below every card
    /// fill, or step 4 reads either none of the soft edge or the whole card as outline. Measured
    /// across the eight `15` §A5 biome bases plus this generator's neutral:
    /// </para>
    /// <list type="bullet">
    ///   <item>the furthest fill from <c>#231A2E</c> is Frostbound Reach's <c>#7EC8E8</c> at ≈264,
    ///   so the furthest inner-edge pixel — a quarter of the way there — is ≈66;</item>
    ///   <item>the nearest fill is Sunken Crypt's <c>#4A6E7A</c> at ≈120.</item>
    /// </list>
    /// <para>
    /// 90 is the midpoint of (66, 120) and clears both bands by more than 20 units. It is a fact
    /// about <em>this generator's palette</em>, not a calibration of `15`, and it says nothing about
    /// what a Midjourney batch's antialiasing needs.
    /// </para>
    /// </remarks>
    public const double OutlineColourTolerance = 90d;

    /// <summary>
    /// Step 4's gap-closure radius. <b>Two pixels.</b>
    /// </summary>
    /// <remarks>
    /// The outline this generator draws is a closed rectangular ring with no break in it, so the
    /// closing has nothing load-bearing to bridge and step 4 comes back having written nothing. Two
    /// pixels is small against the ring's own width (five or more at every canvas
    /// <see cref="GenerationCanvas"/> produces) and small against the card's interior, which is what
    /// keeps the closing from proposing a repair that swallows the subject.
    /// </remarks>
    public const double OutlineGapClosureRadius = 2d;

    /// <summary>
    /// Step 3's palette match tolerance. <b>Eight channel units.</b>
    /// </summary>
    /// <remarks>
    /// Every fill, cross and stamp pixel on a biome card is written as a literal `15` §A5 hue, so it
    /// is at distance zero and snaps to itself; the one layer of antialiased outline is 30 to 66
    /// units from the nearest authorised colour and is left off-palette and counted. Eight is small
    /// enough to keep that separation unambiguous and non-zero so the snap is a real comparison
    /// rather than an equality test.
    /// </remarks>
    public const double PaletteMatchTolerance = 8d;

    /// <summary>
    /// Step 6's colour budget. <b>256 — the size of a PNG-8 palette.</b>
    /// </summary>
    /// <remarks>
    /// Not an invented number: 256 is what a palette PNG holds and what pngquant's own
    /// <c>--colors</c> defaults to, and `15` §B4 step 6 names pngquant. Stating it is what makes the
    /// median cut run at all — M8-06 shipped it having never been run on a real palette, and a batch
    /// that left the budget null would leave it that way.
    /// </remarks>
    public const double ExportColourBudget = 256d;

    /// <summary>
    /// Step 6's accept/reject floor for the median cut. <b>Eight channel units of mean error.</b>
    /// </summary>
    /// <remarks>
    /// A placeholder holds fewer than ten distinct colours before resampling, so a 256-colour median
    /// cut should be lossless and the measured error near zero. Eight is a ceiling generous enough
    /// that the resampled edges cannot trip it and tight enough that a reduction which genuinely
    /// damaged the image would be rejected — and the step reports the measured error either way, so
    /// the batch can say what it actually was rather than what this number allowed.
    /// </remarks>
    public const double ExportMaxMeanError = 8d;

    /// <summary>
    /// Step 5's unsharp-mask radius. <b>One pixel of sigma.</b>
    /// </summary>
    /// <remarks>
    /// `15` §B4 step 5 authorises the amount (0.4) and no radius. One pixel is the smallest sigma
    /// that produces a kernel with real taps on both sides — <c>ResizeStep</c> reaches three sigma,
    /// so this is a seven-tap kernel — and a wider one would spread the card's hard edge into a
    /// visible ring that Part F item 6 would then have to argue about.
    /// </remarks>
    public const double ResizeSharpenRadius = 1d;

    /// <summary>
    /// Step 3's neutral list. <b>Empty, and that is a statement rather than a hole.</b>
    /// </summary>
    /// <remarks>
    /// 🔒 `15` §A5 says a biome asset uses "these six hues plus neutrals" and never enumerates the
    /// neutrals — which is why the key ships null. This generator resolves that for its own output
    /// only, by writing <b>no neutral at all</b> on a biome card: the fill, the cross and the stamp
    /// are three of §A5's own six hues and the outline is §A3's. So the stated list is empty because
    /// the batch contains none, not because nobody thought about it. It is emphatically not a claim
    /// about what §A5's neutrals are; that hole stays open for a human to close.
    /// </remarks>
    public static IReadOnlyList<string> PaletteNeutrals { get; } = [];

    /// <summary>
    /// The threshold set `15` §B4's seven steps run against: the shipped register, with the nine
    /// processing values above stated over it.
    /// </summary>
    public static ThresholdSet ForPipeline() => ThresholdSet.Uncalibrated()
        .With(ThresholdKeys.BackgroundKeyTolerance, BackgroundKeyTolerance)
        .With(ThresholdKeys.MatteDecontaminationStrength, MatteDecontaminationStrength)
        .With(ThresholdKeys.OutlineColourTolerance, OutlineColourTolerance)
        .With(ThresholdKeys.OutlineGapClosureRadius, OutlineGapClosureRadius)
        .With(ThresholdKeys.PaletteMatchTolerance, PaletteMatchTolerance)
        .WithColours(ThresholdKeys.PaletteNeutrals, PaletteNeutrals)
        .With(ThresholdKeys.ResizeSharpenRadius, ResizeSharpenRadius)
        .With(ThresholdKeys.ExportColourBudget, ExportColourBudget)
        .With(ThresholdKeys.ExportMaxMeanError, ExportMaxMeanError);

    /// <summary>
    /// The threshold set `15` Part F is graded against: the shipped register, <b>untouched</b>.
    /// </summary>
    /// <remarks>
    /// 🔒 Every one of the seventeen keys is null here, including the nine
    /// <see cref="ForPipeline"/> states. That is not an oversight and it is not duplication — the
    /// two sets answer two different questions. "What did this generator run the steps with" is a
    /// property of the generator; "what has anybody calibrated the acceptance gate to" is a property
    /// of the project, and today the answer is nothing. Handing the gate the generator's own working
    /// values would let the generator grade itself.
    /// </remarks>
    /// <param name="registerJson">
    /// The contents of <c>assets/pipeline/thresholds.json</c>, read verbatim, so that a key somebody
    /// calibrates later is honoured without a code change here.
    /// </param>
    public static ThresholdSet ForQualityAssurance(string registerJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerJson);
        return ThresholdSet.LoadFrom(registerJson);
    }
}
