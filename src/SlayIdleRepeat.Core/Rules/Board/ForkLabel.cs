namespace SlayIdleRepeat.Core.Rules.Board;

/// <summary>The four fork-branch preview labels.</summary>
internal enum ForkLabel
{
    /// <summary>+Elite, +Enemy, +Treasure, −Shrine.</summary>
    Perilous,

    /// <summary>+Shrine, +Campfire, +Shop, −Enemy.</summary>
    Sheltered,

    /// <summary>+Dice Forge, +Event, +Minigame.</summary>
    Arcane,

    /// <summary>+Cache, +Curse, +Elite.</summary>
    Feral,
}
