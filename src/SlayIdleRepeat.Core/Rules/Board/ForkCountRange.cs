using System.Globalization;

namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>
/// How many forks one stage may carry — the inclusive range
/// <see cref="BoardGenerator"/> draws a stage's fork count from.
/// </summary>
/// <remarks>
/// 🔒 <b>A range, not a floor.</b> <see cref="BoardGenerator"/> stops early when a stage's geometry
/// leaves no viable junction, so an authored <see cref="Maximum"/> the spine cannot hold saturates
/// and stops rather than corrupting the layout. That was already true of the hardcoded 1-2 this
/// replaces; it matters more now that content can name a large number.
/// <para>
/// No upper bound is imposed here. `03` authorises none, and the geometry is the real ceiling —
/// inventing a cap would be a plausible number standing in for a measured one.
/// </para>
/// </remarks>
/// <param name="Minimum">The fewest forks this stage may carry. Zero means the stage may carry none.</param>
/// <param name="Maximum">The most it may carry, inclusive, and never below <see cref="Minimum"/>.</param>
internal readonly record struct ForkCountRange(int Minimum, int Maximum)
{
    /// <summary>Builds a range, refusing one that could never be drawn from.</summary>
    /// <exception cref="ArgumentException">The minimum is negative, or the maximum is below the minimum.</exception>
    public static ForkCountRange Of(int minimum, int maximum, string parameterName)
    {
        if (minimum < 0)
        {
            throw new ArgumentException(
                $"a fork count range's minimum must not be negative; it is {minimum.ToString(CultureInfo.InvariantCulture)}.",
                parameterName);
        }

        if (maximum < minimum)
        {
            throw new ArgumentException(
                $"a fork count range's maximum ({maximum.ToString(CultureInfo.InvariantCulture)}) must not be below its " +
                $"minimum ({minimum.ToString(CultureInfo.InvariantCulture)}).",
                parameterName);
        }

        return new ForkCountRange(minimum, maximum);
    }

    /// <summary>The range `03` §1 authored before fork density became content: one or two per stage.</summary>
    /// <remarks>
    /// Here rather than inline at a call site so the one number the design states has one home, and
    /// so a search for it finds the shipped chapters' authored value beside it rather than a literal
    /// buried in a factory's default.
    /// </remarks>
    public static ForkCountRange Authored { get; } = new(1, 2);
}
