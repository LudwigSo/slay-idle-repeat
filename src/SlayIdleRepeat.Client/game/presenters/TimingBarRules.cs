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
/// </remarks>
public sealed class TimingBarRules
{
    /// <summary>The document the timing bar's numbers live in.</summary>
    public const string DocumentPath = "tuning/minigames.json";

    /// <summary>How many strikes one game gives the player.</summary>
    public int Strikes => throw new NotImplementedException(NotBuiltYet);

    /// <summary>How long one full there-and-back sweep of the cursor takes, in seconds.</summary>
    public double SweepSeconds => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Half the width of the scoring window, measured from the centre of the bar.</summary>
    public double HitWindowHalfWidth => throw new NotImplementedException(NotBuiltYet);

    /// <summary>How far one reduced-motion step moves the cursor, as a fraction of the bar.</summary>
    public double ReducedMotionStepFraction => throw new NotImplementedException(NotBuiltYet);

    /// <summary>Reads the four numbers. Throws rather than defaulting on anything malformed.</summary>
    /// <param name="content">The loaded content set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is null.</exception>
    /// <exception cref="ContentException">The document, a pointer or a value is not usable.</exception>
    public static TimingBarRules Read(ContentSnapshot content) =>
        throw new NotImplementedException(NotBuiltYet);

    private const string NotBuiltYet =
        "TimingBarRules is a signature-only stub: the reader and its strikes-versus-rows refusal " +
        "land with the tuning document the tests are written against.";
}
