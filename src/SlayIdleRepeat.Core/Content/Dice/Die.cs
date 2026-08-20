namespace SlayIdleRepeat.Core.Content.Dice;

/// <summary>The die the run rolls: an ordinary six-sided die showing 1..6 pips.</summary>
/// <remarks>
/// <para>
/// A roll answers a number and nothing else. What HAPPENS after a roll is the board's: the pips are
/// the movement, and the tile the movement lands on decides the rest. There is no per-face effect,
/// no face kind, no tier, and no way for any system to replace a face — the die has one shape and
/// keeps it for the whole run.
/// </para>
/// <para>
/// Lives under <c>Content/Dice</c> rather than <c>Rules/Dice</c> because the layering forbids
/// <c>Events</c> from naming <c>Rules</c>, and <c>DiceRolled</c> carries a roll's pips.
/// </para>
/// </remarks>
public static class Die
{
    /// <summary>The lowest number the die can show.</summary>
    public const int MinPips = 1;

    /// <summary>The highest number the die can show.</summary>
    public const int MaxPips = 6;

    /// <summary>The number of sides.</summary>
    public const int SideCount = MaxPips - MinPips + 1;

    /// <summary>Whether <paramref name="pips"/> is a number this die can show.</summary>
    public static bool IsPips(int pips) => pips is >= MinPips and <= MaxPips;
}
