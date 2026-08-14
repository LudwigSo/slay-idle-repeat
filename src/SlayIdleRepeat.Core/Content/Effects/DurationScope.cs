namespace SlayIdleRepeat.Core.Content.Effects;

/// <summary>🔒 The six duration scopes of `18` §6.</summary>
/// <remarks>
/// 🔒 <b>6.</b> `18` §11: <em>"6 duration scopes = 5 + <c>PHASE</c>"</em>.
/// </remarks>
public enum DurationScope
{
    /// <summary>Applies once and does not persist.</summary>
    INSTANT = 1,

    /// <summary>Ends when the battle ends.</summary>
    BATTLE = 2,

    /// <summary>
    /// Ends when the boss <b>exits the phase in which the effect was applied</b> (`05` §3.1's phase
    /// check fires the exits; `17` §1.1's <c>AURA</c> mechanics are <c>PHASE</c>-scoped by
    /// definition — Gulgrot's Bog Air). Outside a boss fight it behaves as <see cref="BATTLE"/>.
    /// </summary>
    PHASE = 3,

    /// <summary>Ends at the next stage boundary.</summary>
    STAGE = 4,

    /// <summary>Ends when the run ends.</summary>
    RUN = 5,

    /// <summary>Never ends.</summary>
    PERMANENT = 6,
}
