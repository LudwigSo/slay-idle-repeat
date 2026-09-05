using SlayIdleRepeat.Core.Content;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// The four authored numbers the timing-bar minigame is played by, read out of
/// <c>tuning/minigames.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Presentation numbers, and they live on the client side of the boundary on purpose.</b> How
/// fast a cursor sweeps and how wide its window is decide nothing the server adjudicates — the rules
/// layer validates a claimed tier and nothing else — so a reader for them in Core would put a screen's
/// frame rate inside the domain.
/// </para>
/// <para>
/// 🔒 <b><c>strikes + 1</c> must equal the reward table's row count.</b> A strike scores a hit or it
/// does not, so a game of <c>N</c> strikes can end on any of <c>N + 1</c> hit counts and the tier is
/// the hit count. Authored apart, the two documents drift into a game whose best play submits a tier
/// <c>MINIGAME_SUBMIT</c> refuses — so the read refuses it here rather than at the press.
/// </para>
/// <para>
/// 🔴 <b>The table is genuinely OPENED, and an absent one is refused rather than believed.</b> A read
/// that compared the authored strike count against a four it carried in code would satisfy every case
/// about the disagreement and then agree with a retuned table nobody grew — which is the drift the
/// comparison exists to catch. The row count is asked of <c>tuning/currencies.json</c> itself.
/// </para>
/// <para>
/// The minigame id in the reference below is a KEY that document authors, not a transcription of the
/// rules layer's catalogue: <c>MinigameCatalogue</c> is internal to Core, and what this read needs is
/// the member name under <c>#/minigameRewards</c> rather than an enum member.
/// </para>
/// </remarks>
public sealed class TimingBarRules
{
    /// <summary>The document the timing bar's numbers live in.</summary>
    public const string DocumentPath = "tuning/minigames.json";

    /// <summary>Where the reward table whose row count the strike count is held against lives.</summary>
    private const string RewardTableReference =
        "tuning/currencies.json#/minigameRewards/MG_TIMING_BAR";

    private const string StrikesReference = DocumentPath + "#/timingBar/strikes";
    private const string SweepSecondsReference = DocumentPath + "#/timingBar/sweepSeconds";
    private const string HalfWidthReference = DocumentPath + "#/timingBar/hitWindowHalfWidth";
    private const string StepFractionReference =
        DocumentPath + "#/timingBar/reducedMotionStepFraction";

    /// <summary>The widest a half-width can be before the window is the whole bar.</summary>
    private const double WidestHalfWidth = 0.5;

    /// <summary>The whole bar, which is the furthest one step could ever move the cursor.</summary>
    private const double WholeBar = 1.0;

    private TimingBarRules(
        int strikes, double sweepSeconds, double hitWindowHalfWidth, double reducedMotionStepFraction)
    {
        Strikes = strikes;
        SweepSeconds = sweepSeconds;
        HitWindowHalfWidth = hitWindowHalfWidth;
        ReducedMotionStepFraction = reducedMotionStepFraction;
    }

    /// <summary>How many strikes one game gives the player.</summary>
    public int Strikes { get; }

    /// <summary>How long one full there-and-back sweep of the cursor takes, in seconds.</summary>
    public double SweepSeconds { get; }

    /// <summary>Half the width of the scoring window, measured from the centre of the bar.</summary>
    public double HitWindowHalfWidth { get; }

    /// <summary>How far one reduced-motion step moves the cursor, as a fraction of the bar.</summary>
    public double ReducedMotionStepFraction { get; }

    /// <summary>Reads the four numbers. Throws rather than defaulting on anything malformed.</summary>
    /// <param name="content">The loaded content set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="ContentException">The document, a pointer or a value is not usable.</exception>
    public static TimingBarRules Read(ContentSnapshot content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var strikes = content.ReadInt32(StrikesReference);
        var sweepSeconds = content.ReadDouble(SweepSecondsReference);
        var halfWidth = content.ReadDouble(HalfWidthReference);
        var stepFraction = content.ReadDouble(StepFractionReference);

        if (strikes < 1)
        {
            throw new InvalidTunableException(
                StrikesReference,
                "a game of no strikes has one reachable outcome and no way to play for it, so " +
                "there is nothing on the bar to press.");
        }

        if (sweepSeconds <= 0)
        {
            throw new InvalidTunableException(
                SweepSecondsReference,
                "a sweep of no time is a cursor that is everywhere at once, and the window it is " +
                "judged against would be unhittable by anything but luck.");
        }

        if (halfWidth is <= 0 or > WidestHalfWidth)
        {
            throw new InvalidTunableException(
                HalfWidthReference,
                "the scoring window is measured from the centre of the bar as a fraction of it, so " +
                "it has to be wider than nothing and no wider than half — a half-width past " +
                WidestHalfWidth + " is a window covering the whole bar, which is a game every " +
                "strike wins.");
        }

        if (stepFraction is <= 0 or > WholeBar)
        {
            throw new InvalidTunableException(
                StepFractionReference,
                "the reduced-motion step is a fraction of the bar, so it has to move the cursor and " +
                "cannot move it further than the bar is long — a step of nothing leaves the " +
                "accessible arm of this game with no way to aim at all.");
        }

        RefuseAStrikeCountTheTableCannotPay(content, strikes);

        return new TimingBarRules(strikes, sweepSeconds, halfWidth, stepFraction);
    }

    /// <summary>
    /// Holds the authored strike count against the reward table the tier is paid from.
    /// </summary>
    /// <remarks>
    /// 🔴 The table is read rather than assumed. Both halves are content: the strike count is one
    /// document's and the row count is another's, and a game of <c>N</c> strikes ends on one of
    /// <c>N + 1</c> hit counts because a strike either scores or does not. A disagreement is a game
    /// whose best play claims a tier the handler has no row for, refused after the player has played
    /// it — so it is refused here instead, before the screen opens at all.
    /// </remarks>
    private static void RefuseAStrikeCountTheTableCannotPay(ContentSnapshot content, int strikes)
    {
        var table = content.Read(RewardTableReference);

        if (table.Kind != ContentValueKind.Array)
        {
            throw new ContentTypeMismatchException(RewardTableReference, table.Kind, "Array");
        }

        if (table.Items.Count != strikes + 1)
        {
            throw new InvalidTunableException(
                StrikesReference,
                "a game of " + strikes + " strikes ends on one of " + (strikes + 1) +
                " hit counts and the hit count IS the outcome tier, but '" + RewardTableReference +
                "' authors " + table.Items.Count + " rows. Whichever of the two moved, the game " +
                "would be played for a tier MINIGAME_SUBMIT has no row for — or an authored row " +
                "would be unreachable by any play.");
        }
    }
}
