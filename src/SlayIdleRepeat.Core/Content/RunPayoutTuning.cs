using System.Globalization;

namespace SlayIdleRepeat.Core.Content;

/// <summary>
/// <c>FinalPayout = BankedRewards * CompletionMultiplier * AdDoubleMultiplier</c>, read out of
/// <c>tuning/progression.json</c>'s <c>completionMultiplier</c> and <c>adDoubleMultiplier</c> blocks.
/// </summary>
internal sealed class RunPayoutTuning
{
    /// <summary>The document the payout blocks live in.</summary>
    internal const string DocumentPath = "tuning/progression.json";

    private const string CompletionPointer = DocumentPath + "#/completionMultiplier";

    private const string AdDoublePointer = DocumentPath + "#/adDoubleMultiplier/value";

    /// <summary>The boss killed. 1.0 as shipped.</summary>
    internal const string VictoryReference = CompletionPointer + "/VICTORY";

    /// <summary>Death in Stage 3. 0.6 as shipped.</summary>
    internal const string Stage3DeathReference = CompletionPointer + "/STAGE_3_DEATH";

    /// <summary>Death in Stage 2. 0.4 as shipped.</summary>
    internal const string Stage2DeathReference = CompletionPointer + "/STAGE_2_DEATH";

    /// <summary>Death in Stage 1. 0.25 as shipped.</summary>
    internal const string Stage1DeathReference = CompletionPointer + "/STAGE_1_DEATH";

    /// <summary>The run was abandoned; no gear drops are kept. 0.10 as shipped.</summary>
    internal const string AbandonReference = CompletionPointer + "/ABANDON";

    private readonly IReadOnlyDictionary<RunCompletionOutcome, double> _completionMultiplier;
    private readonly double _adDoubleMultiplier;

    private RunPayoutTuning(
        IReadOnlyDictionary<RunCompletionOutcome, double> completionMultiplier, double adDoubleMultiplier)
    {
        _completionMultiplier = completionMultiplier;
        _adDoubleMultiplier = adDoubleMultiplier;
    }

    /// <summary>The completion multiplier for one outcome.</summary>
    internal double CompletionMultiplier(RunCompletionOutcome outcome) => _completionMultiplier[outcome];

    /// <summary>2.0 if the run-end rewarded ad was watched, else 1.0.</summary>
    internal double AdDoubleMultiplier(bool watchedAd) => watchedAd ? _adDoubleMultiplier : 1.0;

    /// <summary>Reads both payout blocks. Throws rather than defaulting on anything unusable.</summary>
    /// <exception cref="MissingContentException">The document or a pointer is not there.</exception>
    /// <exception cref="UnauthorisedTunableException">A pointer holds a deliberate <c>null</c>.</exception>
    /// <exception cref="ContentTypeMismatchException">A leaf holds the wrong shape.</exception>
    /// <exception cref="InvalidTunableException">A value is authorised but unusable.</exception>
    internal static RunPayoutTuning Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var completion = new Dictionary<RunCompletionOutcome, double>
        {
            [RunCompletionOutcome.Victory] = ReadShare(content, VictoryReference),
            [RunCompletionOutcome.Stage1Death] = ReadShare(content, Stage1DeathReference),
            [RunCompletionOutcome.Stage2Death] = ReadShare(content, Stage2DeathReference),
            [RunCompletionOutcome.Stage3Death] = ReadShare(content, Stage3DeathReference),
            [RunCompletionOutcome.Abandon] = ReadShare(content, AbandonReference),
        };

        // 02 §6 / §5.2: death always pays SOMETHING — even a Stage 1 death returns 25%, never zero.
        if (completion[RunCompletionOutcome.Stage1Death] <= 0.0)
        {
            throw new InvalidTunableException(
                Stage1DeathReference,
                "02 §5.2 says death still pays: 'even Stage-1 death returns 25%, never zero.' This " +
                "document authors " + Text(completion[RunCompletionOutcome.Stage1Death]) + ".");
        }

        var adDouble = content.ReadDouble(AdDoublePointer);
        if (!double.IsFinite(adDouble) || adDouble <= 1.0)
        {
            throw new InvalidTunableException(
                AdDoublePointer,
                "AdDoubleMultiplier must be a finite number above 1 — it is a DOUBLING. 02 §5.2 " +
                "authors 2.0; this document authors " + Text(adDouble) + ".");
        }

        return new RunPayoutTuning(completion, adDouble);
    }

    private static double ReadShare(ContentSnapshot content, string reference)
    {
        var value = content.ReadDouble(reference);
        if (!double.IsFinite(value) || value < 0.0 || value > 1.0)
        {
            throw new InvalidTunableException(
                reference,
                "A CompletionMultiplier is a share in [0,1]; this document authors " + Text(value) + ".");
        }

        return value;
    }

    private static string Text(double value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>How a run ended, for <see cref="RunPayoutTuning.CompletionMultiplier"/>.</summary>
internal enum RunCompletionOutcome
{
    /// <summary>The boss was killed.</summary>
    Victory,

    /// <summary>The hero died in Stage 1 (and did not revive, or had no revive left).</summary>
    Stage1Death,

    /// <summary>The hero died in Stage 2.</summary>
    Stage2Death,

    /// <summary>
    /// The hero died in Stage 3 — including a death to the boss itself, which the board has no
    /// stage of its own for. Treated as the closest of the three named stages.
    /// </summary>
    Stage3Death,

    /// <summary>The run was ended voluntarily, without a death and without the boss killed.</summary>
    Abandon,
}
