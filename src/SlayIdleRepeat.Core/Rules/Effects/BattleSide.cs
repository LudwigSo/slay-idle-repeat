namespace SlayIdleRepeat.Core.Rules.Effects;

/// <summary>Which of a battle's two sides an actor fights for.</summary>
/// <remarks>
/// A Ghost Duel is the same code path with two hero-shaped sides: the opposing hero occupies
/// <see cref="ENEMY"/>, so PvE packs and duels share one roster/targeting implementation instead of
/// a duel-shaped branch. No <c>0</c> member, so an uninitialised field can't read as a real side.
/// </remarks>
internal enum BattleSide
{
    /// <summary>The hero's side — hero, pets, and in a duel the attacking hero.</summary>
    HERO = 1,

    /// <summary>The opposing side — enemies, elites, bosses and summons in PvE; the defending hero and its pets in a duel.</summary>
    ENEMY = 2,
}
