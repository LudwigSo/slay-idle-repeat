namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>The six duration scopes.</summary>
public enum DurationScope
{
    /// <summary>Applies once and does not persist.</summary>
    INSTANT = 1,

    /// <summary>Ends when the battle ends.</summary>
    BATTLE = 2,

    /// <summary>
    /// Ends when the boss exits the phase in which the effect was applied. Outside a boss fight it
    /// behaves as <see cref="BATTLE"/>.
    /// </summary>
    PHASE = 3,

    /// <summary>Ends at the next stage boundary.</summary>
    STAGE = 4,

    /// <summary>Ends when the run ends.</summary>
    RUN = 5,

    /// <summary>Never ends.</summary>
    PERMANENT = 6,
}
