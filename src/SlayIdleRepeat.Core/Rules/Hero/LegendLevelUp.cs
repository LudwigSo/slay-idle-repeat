using SlayIdleRepeat.Core.Primitives;

namespace SlayIdleRepeat.Core.Rules.Hero;

/// <summary>What one reconciliation of a player's Legend Level against their lifetime XP would leave behind.</summary>
/// <param name="FromLevel">The level the player stood at.</param>
/// <param name="ToLevel">The level their lifetime XP has bought. Never below <paramref name="FromLevel"/>.</param>
/// <param name="TalentPointsGranted">One grant per level gained, at the authored rate. Zero when no level was gained.</param>
/// <param name="Banks">The Energy banks afterwards — refilled to the new maximum on a level-up, untouched otherwise.</param>
/// <remarks>
/// A value the rule answers rather than a mutation it performs: the rule is a calculator, and the
/// aggregate is where an answer becomes state. That split is what keeps a reconciliation that is
/// never applied — because the command it rode on was refused — from moving a Talent Point.
/// </remarks>
internal readonly record struct LegendLevelUp(
    int FromLevel, int ToLevel, long TalentPointsGranted, EnergyBanks Banks)
{
    /// <summary>Whether the player gained at least one level.</summary>
    internal bool Occurred => ToLevel > FromLevel;

    /// <summary>How many levels were gained.</summary>
    internal int LevelsGained => ToLevel - FromLevel;
}
