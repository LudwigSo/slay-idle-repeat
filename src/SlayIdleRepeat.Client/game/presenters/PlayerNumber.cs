using System.Globalization;

namespace SlayIdleRepeat.Client.Game.Presenters;

/// <summary>
/// How every number a player reads is written: shortened once it passes ten thousand, and exact
/// while they hold it.
/// </summary>
/// <remarks>
/// <para>
/// 🔒 <b>One implementation, in the presenters, because the alternative is several.</b> The rule is
/// the design's and it binds every screen; written inside whichever screen needed it first it would
/// be re-written — slightly differently, with a different rounding direction and a different letter
/// — by the second. It sits here rather than in a scene so the screens that draw a number can be
/// proven to obey it under a test runner, which is the only place anything about this build can be
/// proven at all.
/// </para>
/// <para>
/// 🔒 <b>Shortened DOWNWARD, never rounded up.</b> A price rounded up reads as more than it costs
/// and a balance rounded up reads as more than the player has — and the second is the one that sends
/// somebody to a control they cannot afford. Truncation makes the shortened form a lower bound on
/// the real number in every case, which is the only direction that cannot mislead.
/// </para>
/// <para>
/// ⚠️ The exact value is <see cref="Full"/>, and it is a separate answer rather than a longer format:
/// the design gives it back on a long press, so the two forms are two states of one readout and the
/// screen holding it decides which is on the page.
/// </para>
/// </remarks>
public static class PlayerNumber
{
    /// <summary>
    /// The last value written out in full. Anything past this is shortened.
    /// </summary>
    /// <remarks>
    /// Ten thousand itself is written out: the rule shortens what is ABOVE ten thousand, and a
    /// boundary read the other way would shorten the one value the rule names as the edge.
    /// </remarks>
    public const long ExactUpTo = 10_000;

    /// <summary>What one step up the ladder of suffixes divides by.</summary>
    private const decimal Step = 1_000m;

    /// <summary>The suffix a shortened number carries, by how many steps it was divided.</summary>
    /// <remarks>
    /// Four rungs and no more. Past a trillion the top rung simply keeps more digits in front of it,
    /// which is honest; inventing a letter for a magnitude no screen has ever shown would be this
    /// file deciding a piece of the game's vocabulary on its own.
    /// </remarks>
    private static readonly string[] Suffixes = ["k", "M", "B", "T"];

    /// <summary>One decimal place, which is what the design's own examples carry.</summary>
    private const string ShortenedFormat = "0.0";

    private const string Minus = "-";

    /// <summary>The exact value, in digits, with no grouping and no suffix.</summary>
    /// <param name="value">What to write.</param>
    public static string Full(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The value as a player reads it: exact up to <see cref="ExactUpTo"/>, shortened above it.
    /// </summary>
    /// <param name="value">What to write.</param>
    public static string Abbreviated(long value)
    {
        if (value <= ExactUpTo && value >= -ExactUpTo)
        {
            return Full(value);
        }

        // Carried in decimal rather than in long, so the sign flip below cannot overflow on the one
        // value whose magnitude has no positive counterpart.
        var magnitude = value < 0 ? -(decimal)value : value;
        var steps = -1;

        while (magnitude >= Step && steps < Suffixes.Length - 1)
        {
            magnitude /= Step;
            steps++;
        }

        var shortened = decimal.Truncate(magnitude * 10m) / 10m;
        var text = shortened.ToString(ShortenedFormat, CultureInfo.InvariantCulture) + Suffixes[steps];

        return value < 0 ? Minus + text : text;
    }
}
