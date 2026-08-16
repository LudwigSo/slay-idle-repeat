using System.Globalization;
using SlayIdleRepeat.Core.Content;
using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Hero;

/// <summary>
/// `07` §1.1's Legend XP curve: what one level-up costs, what a level has cost in total, and which
/// level a lifetime total has bought.
/// </summary>
/// <remarks>
/// <para>
/// <b>The formula is authoritative and the document's own totals are not.</b> `07` §1.1 prints a
/// cumulative column ending at ~3.04M for level 200 and then says two lines later that the cap costs
/// ~3.07M. They cannot both be right, and the M4 kickoff ruled that the formula settles it: summing
/// the authored cost over the 199 level-ups a player actually makes reproduces the table's figure,
/// and the prose figure is what you get by summing 200 terms — one too many, because the cost of
/// going from level 200 to 201 is a level-up the cap makes unreachable. Neither rounded total is
/// transcribed here or anywhere else in this codebase; both would be stale the first time the
/// exponent moves.
/// </para>
/// <para>
/// <b>Rounding.</b> The document authors no rounding rule, so this uses the repository's own — four
/// decimal places at every accumulation point. Each level's cost is rounded as it is produced and the
/// running total is rounded as it accumulates, which is what makes the answer independent of how many
/// terms a caller asks for at a time.
/// </para>
/// <para>
/// <see cref="LevelFor"/> walks the ladder rather than inverting the curve. The ladder is at most a
/// couple of hundred rungs, an inverse of a summed power series has no closed form, and a numerical
/// inverse would answer a level the forward function disagrees with at the boundary — which is
/// exactly the value the player's level is compared against.
/// </para>
/// </remarks>
internal static class LegendLevelCurve
{
    /// <summary>What it costs to go from <paramref name="level"/> to the next one.</summary>
    /// <param name="level">A Legend Level at or above the range's minimum, and below its maximum.</param>
    /// <param name="curve">The authored coefficient and exponent.</param>
    /// <param name="range">The authored Legend Level range.</param>
    /// <returns>The Legend XP the level-up costs, rounded to the determinism convention.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="level"/> is outside the range, or is the cap — the cap has no next level, and
    /// answering a cost for one would be pricing a level-up nobody can make.
    /// </exception>
    internal static double XpForLevel(int level, LegendCurveTuning curve, LegendTuning range)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(range);

        if (level < range.Minimum || level >= range.Maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "07 §1.1's LegendXpForLevel(L) is the cost of going from L to L+1, so it is defined " +
                "for " + Render(range.Minimum) + ".." + Render(range.Maximum - 1) + " and for " +
                "nothing else. At the cap of " + Render(range.Maximum) + " there is no next level " +
                "to price, and below the minimum there is no level at all.");
        }

        return DeterminismRounding.Round(curve.Coefficient * Math.Pow(level, curve.Exponent));
    }

    /// <summary>
    /// The lifetime Legend XP a player must have banked to stand at <paramref name="level"/> — the
    /// sum of every level-up below it.
    /// </summary>
    /// <param name="level">A Legend Level inside the authored range. The minimum costs nothing.</param>
    /// <param name="curve">The authored coefficient and exponent.</param>
    /// <param name="range">The authored Legend Level range.</param>
    /// <returns>The cumulative Legend XP, rounded at each accumulation.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="level"/> is outside the range.</exception>
    internal static double CumulativeXpTo(int level, LegendCurveTuning curve, LegendTuning range)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(range);

        if (level < range.Minimum || level > range.Maximum)
        {
            throw new ArgumentOutOfRangeException(
                nameof(level),
                level,
                "No player stands outside " + Render(range.Minimum) + ".." + Render(range.Maximum) +
                ", so no lifetime total buys a level outside it.");
        }

        var total = 0d;

        for (var below = range.Minimum; below < level; below++)
        {
            total = DeterminismRounding.Round(total + XpForLevel(below, curve, range));
        }

        return total;
    }

    /// <summary>The highest Legend Level a lifetime Legend XP total has bought.</summary>
    /// <param name="lifetimeXp">The player's lifetime banked Legend XP. Never negative.</param>
    /// <param name="curve">The authored coefficient and exponent.</param>
    /// <param name="range">The authored Legend Level range.</param>
    /// <returns>
    /// The level, never below the minimum and never above the cap. Excess XP past the cap is kept as
    /// a total and buys nothing, which is what a cap is.
    /// </returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="lifetimeXp"/> is negative.</exception>
    internal static int LevelFor(long lifetimeXp, LegendCurveTuning curve, LegendTuning range)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(range);

        if (lifetimeXp < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetimeXp),
                lifetimeXp,
                "Legend XP is lifetime banked income and is never spent, so a negative total is not " +
                "a player who owes XP — it is a corrupt row, and answering a level for it would " +
                "hide that.");
        }

        var level = range.Minimum;
        var spent = 0d;

        while (level < range.Maximum)
        {
            spent = DeterminismRounding.Round(spent + XpForLevel(level, curve, range));

            if (spent > lifetimeXp)
            {
                break;
            }

            level++;
        }

        return level;
    }

    private static string Render(int value) => value.ToString(CultureInfo.InvariantCulture);
}
